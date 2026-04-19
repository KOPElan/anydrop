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

  function onDragEnter(e) {
    if (!e.dataTransfer || !e.dataTransfer.types.includes('Files')) return;
    clearTimeout(leaveTimer);
    if (!isDragging) {
      isDragging = true;
      dotNetRef.invokeMethodAsync('SetDragging', true);
    }
  }

  function onDragLeave() {
    clearTimeout(leaveTimer);
    leaveTimer = setTimeout(() => {
      if (isDragging) {
        isDragging = false;
        dotNetRef.invokeMethodAsync('SetDragging', false);
      }
    }, 50);
  }

  function onDragOver(e) {
    if (e.dataTransfer && e.dataTransfer.types.includes('Files')) {
      e.preventDefault();
    }
  }

  function onDrop(e) {
    e.preventDefault();
    clearTimeout(leaveTimer);
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
 * @param {HTMLElement} element - 需要滚动到底的容器
 */
AnyDropInterop.scrollToBottom = function (element) {
  if (!element) return;
  element.scrollTop = element.scrollHeight;
  // 延迟补偿：图片等资源加载完成后会撑高容器，需要再次滚到底
  setTimeout(() => { if (element) element.scrollTop = element.scrollHeight; }, 300);
};
