window.AnyDropInterop = window.AnyDropInterop || {
  noop: () => {}
};

// 保存每个元素的清理函数，避免重复注册
const _dropZoneCleanups = new WeakMap();

/**
 * 在指定容器上设置文件拖放区域。
 * 使用 50ms 延迟消抖，避免子元素 dragenter/dragleave 导致的频繁状态切换。
 * 松手后通过 IJSStreamReference 将文件流式传输到 .NET，无需文件对话框。
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

    // 逐文件通过 IJSStreamReference 流式传输到 .NET，避免弹出文件对话框
    (async () => {
      for (const file of files) {
        try {
          const streamRef = DotNet.createJSStreamReference(file);
          await dotNetRef.invokeMethodAsync(
            'ReceiveDroppedFile',
            file.name,
            file.type || 'application/octet-stream',
            file.size,
            streamRef
          );
        } catch (err) {
          console.error('[AnyDrop] Failed to send dropped file:', file.name, err);
        }
      }
    })();
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

/**
 * 将滚动容器滚动到底部（用于聊天消息列表）。
 * 额外在 300ms 后再次滚动，确保图片等异步内容加载后仍然处于底部。
 * 300ms 是实践中覆盖大多数网络图片首次渲染延迟的经验值（< 100ms 通常不够，> 500ms 用户感知明显）。
 * @param {HTMLElement} element - 需要滚动到底的容器
 */
AnyDropInterop.scrollToBottom = function (element) {
  if (!element) return;
  element.scrollTop = element.scrollHeight;
  // 延迟补偿：图片等资源加载完成后会撑高容器，需要再次滚到底
  const SCROLL_DELAY_MS = 300;
  setTimeout(() => { if (element) element.scrollTop = element.scrollHeight; }, SCROLL_DELAY_MS);
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
  element.scrollTop = element.scrollHeight;
  // 延迟补偿：图片等资源加载完成后会撑高容器，需要再次滚到底（同 scrollToBottom 的处理逻辑）
  setTimeout(() => { if (element) element.scrollTop = element.scrollHeight; }, 300);
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
