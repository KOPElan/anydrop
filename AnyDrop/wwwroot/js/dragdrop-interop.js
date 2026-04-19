window.AnyDropInterop = window.AnyDropInterop || {
  noop: () => {}
};

// 保存每个元素的清理函数，避免重复注册
const _dropZoneCleanups = new WeakMap();

/**
 * 在指定容器上设置文件拖放区域。
 * 使用 50ms 延迟消抖，避免子元素 dragenter/dragleave 导致的频繁状态切换。
 * @param {HTMLElement} element - 作为拖放目标的容器
 * @param {DotNetObjectReference} dotNetRef - Blazor 组件的 .NET 引用，用于回调 SetDragging
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
    isDragging = false;
    // 实际文件由 Blazor InputFile 的 OnChange 事件处理
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
 * @param {HTMLElement} element - 需要滚动到底的容器
 */
AnyDropInterop.scrollToBottom = function (element) {
  if (element) {
    element.scrollTop = element.scrollHeight;
  }
};
