window.AnyDropInterop = window.AnyDropInterop || {
  noop: () => {}
};

/**
 * 在指定容器上设置文件拖放区域。
 * 使用 50ms 延迟消抖，避免子元素 dragenter/dragleave 导致的频繁状态切换。
 * @param {HTMLElement} element - 作为拖放目标的容器
 * @param {DotNetObjectReference} dotNetRef - Blazor 组件的 .NET 引用，用于回调 SetDragging
 */
AnyDropInterop.setupDropZone = function (element, dotNetRef) {
  if (!element) return;

  let isDragging = false;
  let leaveTimer = null;

  element.addEventListener('dragenter', (e) => {
    if (!e.dataTransfer || !e.dataTransfer.types.includes('Files')) return;
    clearTimeout(leaveTimer);
    if (!isDragging) {
      isDragging = true;
      dotNetRef.invokeMethodAsync('SetDragging', true);
    }
  });

  element.addEventListener('dragleave', () => {
    clearTimeout(leaveTimer);
    leaveTimer = setTimeout(() => {
      if (isDragging) {
        isDragging = false;
        dotNetRef.invokeMethodAsync('SetDragging', false);
      }
    }, 50);
  });

  element.addEventListener('dragover', (e) => {
    if (e.dataTransfer && e.dataTransfer.types.includes('Files')) {
      e.preventDefault();
    }
  });

  element.addEventListener('drop', (e) => {
    e.preventDefault();
    clearTimeout(leaveTimer);
    isDragging = false;
    // 实际文件由 Blazor InputFile 的 OnChange 事件处理
  });
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
