window.AnyDropInterop = window.AnyDropInterop || {
  noop: () => {}
};

// 保存每个元素的清理函数，避免重复注册
const _dropZoneCleanups = new WeakMap();
const _messageScrollCleanups = new WeakMap();
const _uploadFileCache = new Map();
const _uploadFileCacheTtlMs = 10 * 60 * 1000;
const _uploadFileCacheCleanupIntervalMs = 60 * 1000;
// 快网兜底：覆盖资源很快完成布局但未触发媒体事件的场景
const _fastNetworkFallbackMs = 180;
// 慢网兜底：覆盖较慢资源加载完成后的高度变化
const _slowNetworkFallbackMs = 600;
let _uploadFileCacheCleanupTimer = null;

function _startUploadFileCacheCleanup() {
  if (_uploadFileCacheCleanupTimer) return;
  _uploadFileCacheCleanupTimer = setInterval(() => {
    if (_uploadFileCache.size === 0) {
      clearInterval(_uploadFileCacheCleanupTimer);
      _uploadFileCacheCleanupTimer = null;
      return;
    }

    const now = Date.now();
    for (const [tempId, cached] of _uploadFileCache.entries()) {
      if (!cached || typeof cached.cachedAt !== 'number') {
        _uploadFileCache.delete(tempId);
        continue;
      }
      if (now - cached.cachedAt > _uploadFileCacheTtlMs) {
        _uploadFileCache.delete(tempId);
      }
    }
  }, _uploadFileCacheCleanupIntervalMs);
}

function _trackUploadCacheEntry(tempId, file, context) {
  _uploadFileCache.set(tempId, { file, context, cachedAt: Date.now() });
  _startUploadFileCacheCleanup();
}

AnyDropInterop.cleanupUploadCache = function () {
  _uploadFileCache.clear();
  if (_uploadFileCacheCleanupTimer) {
    clearInterval(_uploadFileCacheCleanupTimer);
    _uploadFileCacheCleanupTimer = null;
  }
};

AnyDropInterop._markMediaObserved = function (media) {
  if (!media || media.dataset.anydropObserved === '1') return false;
  media.dataset.anydropObserved = '1';
  return true;
};

AnyDropInterop._cleanupObservedMediaMarks = function (element) {
  if (!element) return;
  const observedMediaNodes = element.querySelectorAll('img[data-anydrop-observed="1"],video[data-anydrop-observed="1"]');
  const maxObservedNodes = 200;
  if (observedMediaNodes.length <= maxObservedNodes) return;
  const clearCount = observedMediaNodes.length - maxObservedNodes;
  for (let i = 0; i < clearCount; i++) {
    delete observedMediaNodes[i].dataset.anydropObserved;
  }
};

/**
 * 通过 XMLHttpRequest 上传文件列表到 /api/v1/files，支持进度报告。
 * 上传前先通过 GetUploadContext() 从 Blazor 获取当前的 topicId 和 burnAfterReading 设置。
 *
 * @param {File[]} files - 待上传的文件列表
 * @param {DotNetObjectReference} dotNetRef - Blazor 组件的 .NET 引用
 */
AnyDropInterop._uploadFiles = async function (files, dotNetRef) {
  const context = await dotNetRef.invokeMethodAsync('GetUploadContext');
  if (!context || !context.topicId) {
    await dotNetRef.invokeMethodAsync('OnNoTopicSelected');
    return;
  }

  for (const file of files) {
    await AnyDropInterop._uploadSingleFile(file, dotNetRef, context);
  }
};

AnyDropInterop._createTempId = function () {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
};

AnyDropInterop._uploadSingleFile = async function (file, dotNetRef, context) {
  const tempId = AnyDropInterop._createTempId();
  const mimeType = file.type || 'application/octet-stream';

  _trackUploadCacheEntry(tempId, file, context);

  await dotNetRef.invokeMethodAsync(
    'OnFileUploadStarted',
    tempId,
    context.topicId,
    file.name,
    mimeType,
    file.size
  );

  if (context.maxFileSizeBytes > 0 && file.size > context.maxFileSizeBytes) {
    await dotNetRef.invokeMethodAsync('OnFileUploadFailed', tempId, `FILE_TOO_LARGE:${context.maxFileSizeBytes}`);
    return tempId;
  }

  await new Promise((resolve) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', '/api/v1/files');

    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) {
        const percent = Math.round((e.loaded / e.total) * 100);
        dotNetRef.invokeMethodAsync('OnFileUploadProgress', tempId, percent).catch((err) => {
          console.warn('[AnyDrop] Failed to report upload progress:', err);
        });
      }
    };

    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        _uploadFileCache.delete(tempId);
        dotNetRef.invokeMethodAsync('OnFileUploadCompleted', tempId, xhr.responseText).catch((err) => {
          console.warn('[AnyDrop] Failed to notify upload completion:', err);
        });
      } else {
        let msg = `HTTP ${xhr.status}`;
        try {
          const body = JSON.parse(xhr.responseText);
          if (body && body.error) msg = body.error;
        } catch {}
        console.warn('[AnyDrop] Upload failed:', msg);
        dotNetRef.invokeMethodAsync('OnFileUploadFailed', tempId, msg).catch((err) => {
          console.warn('[AnyDrop] Failed to notify upload failure:', err);
        });
      }
      resolve();
    };

    xhr.onerror = () => {
      console.warn('[AnyDrop] XHR network error during upload of', file.name);
      dotNetRef.invokeMethodAsync('OnFileUploadFailed', tempId, 'Network error').catch((err) => {
        console.warn('[AnyDrop] Failed to notify upload error:', err);
      });
      resolve();
    };

    xhr.onabort = () => {
      console.warn('[AnyDrop] Upload aborted for', file.name);
      dotNetRef.invokeMethodAsync('OnFileUploadFailed', tempId, 'Upload aborted').catch((err) => {
        console.warn('[AnyDrop] Failed to notify upload abort:', err);
      });
      resolve();
    };

    const formData = new FormData();
    formData.append('file', file);
    formData.append('topicId', context.topicId);
    formData.append('burnAfterReading', context.burnAfterReading ? 'true' : 'false');

    xhr.send(formData);
  });

  return tempId;
};

AnyDropInterop.retryUpload = async function (tempId, dotNetRef) {
  const cached = _uploadFileCache.get(tempId);
  if (!cached || !cached.file || !cached.context || !cached.context.topicId) {
    return false;
  }

  _uploadFileCache.delete(tempId);
  await AnyDropInterop._uploadSingleFile(cached.file, dotNetRef, cached.context);
  return true;
};

/**
 * 为原生 <input type="file"> 元素绑定 change 事件，触发 HTTP 上传流程。
 * 应在 OnAfterRenderAsync(firstRender) 中调用一次。
 *
 * @param {HTMLInputElement} element - 文件输入元素
 * @param {DotNetObjectReference} dotNetRef - Blazor 组件的 .NET 引用
 */
AnyDropInterop.setupFileInput = function (element, dotNetRef) {
  if (!element || element._anyDropHandlerAttached) return;
  element._anyDropHandlerAttached = true;

  element.addEventListener('change', () => {
    const files = Array.from(element.files || []);
    // 重置 value，允许用户重复选择同一个文件
    element.value = '';
    if (files.length === 0) return;
    AnyDropInterop._uploadFiles(files, dotNetRef);
  });
};

/**
 * 在指定容器上设置文件拖放区域。
 * 使用 50ms 延迟消抖，避免子元素 dragenter/dragleave 导致的频繁状态切换。
 * 松手后直接通过 HTTP multipart 上传文件，不再使用 SignalR 流。
 * @param {HTMLElement} element - 作为拖放目标的容器
 * @param {DotNetObjectReference} dotNetRef - Blazor 组件的 .NET 引用
 */
AnyDropInterop.setupDropZone = function (element, dotNetRef) {
  if (!element) return;

  // 若已注册过，先清理旧监听器，再重新注册（防止重复监听）
  AnyDropInterop.cleanupDropZone(element);

  let isDragging = false;
  let leaveTimer = null;
  let dragCounter = 0; // Track dragenter/dragleave pairs to handle child elements

  function onDragEnter(e) {
    if (!e.dataTransfer || !e.dataTransfer.types.includes('Files')) return;
    dragCounter++;
    clearTimeout(leaveTimer);
    if (!isDragging) {
      isDragging = true;
      dotNetRef.invokeMethodAsync('SetDragging', true);
    }
  }

  function onDragLeave() {
    dragCounter--;
    clearTimeout(leaveTimer);
    // Only hide the overlay when all drag events have left
    if (dragCounter === 0) {
      leaveTimer = setTimeout(() => {
        if (isDragging && dragCounter === 0) {
          isDragging = false;
          dotNetRef.invokeMethodAsync('SetDragging', false);
        }
      }, 50);
    }
  }

  function onDragOver(e) {
    if (e.dataTransfer && e.dataTransfer.types.includes('Files')) {
      e.preventDefault();
    }
  }

  function onDrop(e) {
    e.preventDefault();
    clearTimeout(leaveTimer);
    dragCounter = 0; // Reset counter on drop
    if (isDragging) {
      isDragging = false;
      dotNetRef.invokeMethodAsync('SetDragging', false);
    }

    const files = e.dataTransfer ? Array.from(e.dataTransfer.files) : [];
    if (files.length === 0) return;

    // 直接通过 HTTP multipart 上传，不再使用 SignalR 流式传输
    AnyDropInterop._uploadFiles(files, dotNetRef);
  }

  element.addEventListener('dragenter', onDragEnter);
  element.addEventListener('dragleave', onDragLeave);
  element.addEventListener('dragover', onDragOver);
  element.addEventListener('drop', onDrop);

  // 保存清理函数以便后续调用
  _dropZoneCleanups.set(element, () => {
    clearTimeout(leaveTimer);
    element.removeEventListener('dragenter', onDragEnter);
    element.removeEventListener('dragleave', onDragLeave);
    element.removeEventListener('dragover', onDragOver);
    element.removeEventListener('drop', onDrop);
  });
};

/**
 * 清理指定元素上的拖放事件监听器，防止内存泄漏。
 * @param {HTMLElement} element - 需要清理的容器
 */
AnyDropInterop.cleanupDropZone = function (element) {
  if (!element) return;
  const cleanup = _dropZoneCleanups.get(element);
  if (cleanup) {
    cleanup();
    _dropZoneCleanups.delete(element);
  }
};

AnyDropInterop.setupMessageScrollObserver = function (element, dotNetRef) {
  if (!element || !dotNetRef) return;

  const existingCleanup = _messageScrollCleanups.get(element);
  if (existingCleanup) {
    existingCleanup();
  }

  let rafId = 0;
  let lastIsNearBottom = null;

  const notifyIfChanged = () => {
    if (!element) return;
    const distanceFromBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
    const isNearBottom = distanceFromBottom <= 120;
    if (lastIsNearBottom === isNearBottom) return;
    lastIsNearBottom = isNearBottom;
    dotNetRef.invokeMethodAsync('OnMessageListScrollPositionChanged', isNearBottom)
      .catch((err) => {
        if (err && typeof err.message === 'string' && err.message.includes('disposed')) return;
        console.debug('[AnyDrop] message scroll observer callback failed:', err);
      });
  };

  const onScroll = () => {
    if (rafId) return;
    rafId = requestAnimationFrame(() => {
      rafId = 0;
      notifyIfChanged();
    });
  };

  element.addEventListener('scroll', onScroll, { passive: true });
  notifyIfChanged();

  const cleanup = () => {
    element.removeEventListener('scroll', onScroll);
    if (rafId) {
      cancelAnimationFrame(rafId);
      rafId = 0;
    }
  };
  _messageScrollCleanups.set(element, cleanup);
};

AnyDropInterop.cleanupMessageScrollObserver = function (element) {
  if (!element) return;
  const cleanup = _messageScrollCleanups.get(element);
  if (cleanup) {
    cleanup();
    _messageScrollCleanups.delete(element);
  }
};

/**
 * 将滚动容器滚动到底部（用于聊天消息列表）。
 * 额外在 300ms 后再次滚动，确保图片等异步内容加载后仍然处于底部。
 * 300ms 是实践中覆盖大多数网络图片首次渲染延迟的经验值（< 100ms 通常不够，> 500ms 用户感知明显）。
 * @param {HTMLElement} element - 需要滚动到底的容器
 */
AnyDropInterop._scrollElementToBottom = function (element) {
  if (!element) return;
  element.scrollTo({ top: element.scrollHeight, behavior: 'smooth' });
};

/**
 * 自适应底部滚动补偿：优先监听图片/视频加载事件进行补偿，避免固定延迟在快慢网络场景下失配。
 */
AnyDropInterop._scheduleBottomStabilization = function (element) {
  if (!element) return;
  AnyDropInterop._scrollElementToBottom(element);

  requestAnimationFrame(() => {
    requestAnimationFrame(() => AnyDropInterop._scrollElementToBottom(element));
  });

  const stabilizeScroll = () => AnyDropInterop._scrollElementToBottom(element);
  const mediaNodes = element.querySelectorAll('img,video');
  for (const media of mediaNodes) {
    if (!AnyDropInterop._markMediaObserved(media)) continue;
    const tag = media.tagName;
    const isLoaded = (tag === 'IMG' && media.complete) || (tag === 'VIDEO' && media.readyState >= 2);
    if (isLoaded) continue;

    media.addEventListener('load', stabilizeScroll, { once: true });
    media.addEventListener('error', stabilizeScroll, { once: true });
    media.addEventListener('loadeddata', stabilizeScroll, { once: true });
  }

  setTimeout(stabilizeScroll, _fastNetworkFallbackMs);
  setTimeout(stabilizeScroll, _slowNetworkFallbackMs);
  AnyDropInterop._cleanupObservedMediaMarks(element);
};

AnyDropInterop.scrollToBottom = function (element) {
  AnyDropInterop._scheduleBottomStabilization(element);
};

/**
 * 仅当用户已处于列表底部附近时才自动滚动到底部（用于收到新消息时的条件滚动）。
 * 若用户已手动向上滚动超过 threshold 像素，则不自动滚动，尊重用户的阅读位置。
 * @param {HTMLElement} element - 滚动容器
 * @param {number} [threshold=150] - 距底部多少像素以内视为"底部附近"
 */
AnyDropInterop.scrollToBottomIfNearBottom = function (element, threshold = 150) {
  if (!element) return;
  const distanceFromBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
  if (distanceFromBottom > threshold) return;
  AnyDropInterop._scheduleBottomStabilization(element);
};

/**
 * 滚动到指定消息并触发高亮动画（从搜索页跳转回聊天时使用）。
 * @param {string} messageId - 目标消息的 data-message-id 属性值
 */
AnyDropInterop.scrollToMessage = function (messageId) {
  if (!messageId) return;
  // rAF 确保 Blazor 已将元素渲染到 DOM
  requestAnimationFrame(() => {
    // 使用 CSS.escape() 防止 messageId 中包含特殊 CSS 选择器字符时出错
    const el = document.querySelector(`[data-message-id="${CSS.escape(messageId)}"]`);
    if (!el) return;
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    // 添加高亮动画类，2.5 秒后移除
    el.classList.add('message-highlight');
    setTimeout(() => el.classList.remove('message-highlight'), 2500);
  });
};


/**
 * 触发指定 id 的 <input type="date"> 打开系统日历选择器。
 * @param {string} inputId - input 元素的 id
 */
AnyDropInterop.showDatePicker = function (inputId) {
  const el = document.getElementById(inputId);
  if (el && typeof el.showPicker === 'function') {
    el.showPicker();
  }
};

/**
 * 返回浏览器当前的 IANA 时区 ID（如 "Asia/Shanghai"）。
 * @returns {string} 浏览器时区 ID
 */
AnyDropInterop.getBrowserTimeZone = function () {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
  } catch {
    return 'UTC';
  }
};

/**
 * 触发文件选择输入框的点击事件，用于打开系统文件选择器。
 * 桌面端按钮和移动端浮动面板均使用此方法触发同一对 <input type="file"> 元素。
 * @param {HTMLInputElement} element - 文件输入元素
 */
AnyDropInterop.triggerClick = function (element) {
  if (!element) {
    console.warn('[AnyDrop] triggerClick: element is null or undefined, file picker could not be opened.');
    return;
  }
  element.click();
};
