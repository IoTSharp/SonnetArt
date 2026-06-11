async function createDownloadHref(url) {
  if (!url) {
    throw new Error('没有可下载的图片地址。');
  }

  if (url.startsWith('data:') || url.startsWith('blob:')) {
    return { href: url, revoke: null };
  }

  try {
    const response = await fetch(url, { mode: 'cors' });
    if (!response.ok) {
      throw new Error(`图片下载失败：${response.status}。`);
    }

    const blob = await response.blob();
    const objectUrl = URL.createObjectURL(blob);
    return { href: objectUrl, revoke: () => URL.revokeObjectURL(objectUrl) };
  } catch (error) {
    if (url.startsWith('http://') || url.startsWith('https://')) {
      return { href: url, revoke: null };
    }

    throw error;
  }
}

async function downloadWithAnchor(url, fileName) {
  const download = await createDownloadHref(url);
  const anchor = document.createElement('a');
  anchor.download = fileName || 'image.png';
  anchor.href = download.href;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';

  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();

  if (download.revoke) {
    setTimeout(download.revoke, 1000);
  }

  return { savedLocally: false, fileName: anchor.download };
}

function downloadBytes(base64, fileName, contentType) {
  if (!base64) {
    throw new Error('没有可下载的文件内容。');
  }

  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }

  const blob = new Blob([bytes], { type: contentType || 'application/octet-stream' });
  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.download = fileName || 'download.bin';
  anchor.href = objectUrl;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';

  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);

  return { savedLocally: false, fileName: anchor.download };
}

const systemThemeWatchers = new Map();
const previewEditors = new WeakMap();

function getPreviewEditor(canvas) {
  if (!canvas) {
    return null;
  }

  return previewEditors.get(canvas) || null;
}

function getCanvasPoint(canvas, event) {
  const rect = canvas.getBoundingClientRect();
  const source = event.touches && event.touches.length > 0 ? event.touches[0] : event;
  const scaleX = rect.width > 0 ? canvas.width / rect.width : 1;
  const scaleY = rect.height > 0 ? canvas.height / rect.height : 1;
  return {
    x: (source.clientX - rect.left) * scaleX,
    y: (source.clientY - rect.top) * scaleY,
  };
}

function cssPixels(value) {
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function getRenderedImageRect(image) {
  const rect = image.getBoundingClientRect();
  const styles = window.getComputedStyle(image);
  const paddingLeft = cssPixels(styles.paddingLeft);
  const paddingRight = cssPixels(styles.paddingRight);
  const paddingTop = cssPixels(styles.paddingTop);
  const paddingBottom = cssPixels(styles.paddingBottom);
  const contentWidth = Math.max(1, rect.width - paddingLeft - paddingRight);
  const contentHeight = Math.max(1, rect.height - paddingTop - paddingBottom);
  const naturalWidth = image.naturalWidth || contentWidth;
  const naturalHeight = image.naturalHeight || contentHeight;
  const imageRatio = naturalWidth / Math.max(1, naturalHeight);
  const boxRatio = contentWidth / Math.max(1, contentHeight);

  let width = contentWidth;
  let height = contentHeight;
  if (imageRatio > boxRatio) {
    height = width / Math.max(0.0001, imageRatio);
  } else {
    width = height * imageRatio;
  }

  return {
    left: rect.left + paddingLeft + (contentWidth - width) / 2,
    top: rect.top + paddingTop + (contentHeight - height) / 2,
    width,
    height,
    naturalWidth,
    naturalHeight,
  };
}

function resizePreviewCanvas(canvas, image, editor) {
  if (!canvas || !image) {
    return;
  }

  const rect = getRenderedImageRect(image);
  const stageRect = canvas.offsetParent
    ? canvas.offsetParent.getBoundingClientRect()
    : { left: 0, top: 0 };
  const width = Math.max(1, Math.round(rect.width));
  const height = Math.max(1, Math.round(rect.height));
  canvas.style.left = `${rect.left - stageRect.left}px`;
  canvas.style.top = `${rect.top - stageRect.top}px`;
  canvas.style.width = `${width}px`;
  canvas.style.height = `${height}px`;

  if (canvas.width === width && canvas.height === height) {
    editor.naturalWidth = rect.naturalWidth || width;
    editor.naturalHeight = rect.naturalHeight || height;
    return;
  }

  const previous = document.createElement('canvas');
  previous.width = canvas.width;
  previous.height = canvas.height;
  if (canvas.width > 0 && canvas.height > 0) {
    previous.getContext('2d').drawImage(canvas, 0, 0);
  }

  canvas.width = width;
  canvas.height = height;

  const ctx = canvas.getContext('2d');
  ctx.clearRect(0, 0, width, height);
  if (previous.width > 0 && previous.height > 0 && editor && editor.hasPaint) {
    ctx.drawImage(previous, 0, 0, previous.width, previous.height, 0, 0, width, height);
  }

  editor.naturalWidth = rect.naturalWidth || width;
  editor.naturalHeight = rect.naturalHeight || height;
}

function drawPreviewStroke(canvas, editor, point) {
  const ctx = canvas.getContext('2d');
  const size = editor.brushSize || 36;
  ctx.globalCompositeOperation = 'source-over';
  ctx.fillStyle = 'rgba(45, 212, 191, 0.42)';
  ctx.strokeStyle = 'rgba(45, 212, 191, 0.78)';
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';
  ctx.lineWidth = size;

  if (editor.lastPoint) {
    ctx.beginPath();
    ctx.moveTo(editor.lastPoint.x, editor.lastPoint.y);
    ctx.lineTo(point.x, point.y);
    ctx.stroke();
  } else {
    ctx.beginPath();
    ctx.arc(point.x, point.y, size / 2, 0, Math.PI * 2);
    ctx.fill();
  }

  editor.lastPoint = point;
  editor.hasPaint = true;
}

function beginPreviewStroke(canvas, editor, event) {
  event.preventDefault();
  canvas.setPointerCapture?.(event.pointerId);
  editor.drawing = true;
  editor.lastPoint = null;
  drawPreviewStroke(canvas, editor, getCanvasPoint(canvas, event));
}

function continuePreviewStroke(canvas, editor, event) {
  if (!editor.drawing) {
    return;
  }

  event.preventDefault();
  drawPreviewStroke(canvas, editor, getCanvasPoint(canvas, event));
}

function endPreviewStroke(canvas, editor, event) {
  if (!editor.drawing) {
    return;
  }

  event.preventDefault();
  editor.drawing = false;
  editor.lastPoint = null;
  canvas.releasePointerCapture?.(event.pointerId);
}

function removePreviewListeners(editor) {
  if (!editor || !editor.listeners) {
    return;
  }

  for (const [target, type, listener] of editor.listeners) {
    target.removeEventListener(type, listener);
  }

  editor.listeners = [];
}

function bindPreviewEditor(canvas, image, brushSize) {
  if (!canvas || !image) {
    return;
  }

  let editor = getPreviewEditor(canvas);
  if (!editor) {
    editor = {
      brushSize: Number(brushSize) || 36,
      drawing: false,
      hasPaint: false,
      lastPoint: null,
      listeners: [],
    };
    previewEditors.set(canvas, editor);
  }

  removePreviewListeners(editor);
  editor.brushSize = Number(brushSize) || editor.brushSize || 36;
  resizePreviewCanvas(canvas, image, editor);

  const down = event => beginPreviewStroke(canvas, editor, event);
  const move = event => continuePreviewStroke(canvas, editor, event);
  const up = event => endPreviewStroke(canvas, editor, event);
  const resize = () => resizePreviewCanvas(canvas, image, editor);

  canvas.addEventListener('pointerdown', down);
  canvas.addEventListener('pointermove', move);
  canvas.addEventListener('pointerup', up);
  canvas.addEventListener('pointercancel', up);
  window.addEventListener('resize', resize);
  editor.listeners.push(
    [canvas, 'pointerdown', down],
    [canvas, 'pointermove', move],
    [canvas, 'pointerup', up],
    [canvas, 'pointercancel', up],
    [window, 'resize', resize],
  );
}

function exportPreviewMask(canvas) {
  const editor = getPreviewEditor(canvas);
  if (!canvas || !editor || !editor.hasPaint) {
    return '';
  }

  const output = document.createElement('canvas');
  output.width = editor.naturalWidth || canvas.width;
  output.height = editor.naturalHeight || canvas.height;
  const source = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height);
  const normalized = document.createElement('canvas');
  normalized.width = canvas.width;
  normalized.height = canvas.height;
  const normalizedContext = normalized.getContext('2d');
  const data = source.data;
  for (let index = 0; index < data.length; index += 4) {
    const marked = data[index + 3] > 0;
    data[index] = 255;
    data[index + 1] = 255;
    data[index + 2] = 255;
    data[index + 3] = marked ? 0 : 255;
  }

  normalizedContext.putImageData(source, 0, 0);
  output.getContext('2d').drawImage(normalized, 0, 0, output.width, output.height);
  return output.toDataURL('image/png');
}

window.sonnetArt = {
  prefersDarkTheme: function () {
    return Boolean(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
  },
  watchSystemTheme: function (dotNetReference) {
    if (!window.matchMedia || !dotNetReference) {
      return '';
    }

    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    const id = window.crypto && window.crypto.randomUUID ? window.crypto.randomUUID() : String(Date.now() + Math.random());
    const listener = event => {
      dotNetReference.invokeMethodAsync('OnSystemThemeChanged', Boolean(event.matches));
    };

    if (mediaQuery.addEventListener) {
      mediaQuery.addEventListener('change', listener);
    } else if (mediaQuery.addListener) {
      mediaQuery.addListener(listener);
    }

    systemThemeWatchers.set(id, { mediaQuery, listener });
    return id;
  },
  unwatchSystemTheme: function (id) {
    const watcher = systemThemeWatchers.get(id);
    if (!watcher) {
      return;
    }

    if (watcher.mediaQuery.removeEventListener) {
      watcher.mediaQuery.removeEventListener('change', watcher.listener);
    } else if (watcher.mediaQuery.removeListener) {
      watcher.mediaQuery.removeListener(watcher.listener);
    }

    systemThemeWatchers.delete(id);
  },
  setDocumentTheme: function (theme) {
    const effectiveTheme = theme === 'dark' ? 'dark' : 'light';
    document.documentElement.dataset.sonnetartTheme = effectiveTheme;
    document.documentElement.style.colorScheme = effectiveTheme;
  },
  clearLaunchCredentials: function () {
    const url = new URL(window.location.href);
    let changed = false;
    for (const key of ['token', 'user_id']) {
      if (url.searchParams.has(key)) {
        url.searchParams.delete(key);
        changed = true;
      }
    }

    if (changed) {
      const next = `${url.pathname}${url.search}${url.hash}`;
      window.history.replaceState(window.history.state, document.title, next || '/');
    }
  },
  applySiteBranding: function (branding) {
    if (!branding) {
      return;
    }

    if (branding.title) {
      document.title = branding.title;
    }

    if (branding.description) {
      let meta = document.querySelector('meta[name="description"]');
      if (!meta) {
        meta = document.createElement('meta');
        meta.setAttribute('name', 'description');
        document.head.appendChild(meta);
      }

      meta.setAttribute('content', branding.description);
    }

    if (branding.iconUrl) {
      for (const selector of ['link[rel="icon"]', 'link[rel="alternate icon"]']) {
        const link = document.querySelector(selector);
        if (link) {
          link.setAttribute('href', branding.iconUrl);
        }
      }
    }
  },
  window: {
    invoke: async function (command) {
      const host = globalThis.nativeWeb;
      const api = host && host.window;
      let action = api && api[command];
      if (typeof action !== 'function' && command === 'exit' && host) {
        action = function () {
          return host.invoke('window.exit');
        };
      }

      if (typeof action !== 'function') {
        return false;
      }

      await action.call(api);
      return true;
    },
  },
  download: async function (url, fileName) {
    return await downloadWithAnchor(url, fileName);
  },
  downloadBytes: function (base64, fileName, contentType) {
    return downloadBytes(base64, fileName, contentType);
  },
  copyText: async function (text) {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text || '');
      return;
    }

    const input = document.createElement('textarea');
    input.value = text || '';
    input.setAttribute('readonly', '');
    input.style.position = 'fixed';
    input.style.opacity = '0';
    document.body.appendChild(input);
    input.select();
    document.execCommand('copy');
    input.remove();
  },
  previewEditor: {
    attach: function (canvas, image, brushSize) {
      bindPreviewEditor(canvas, image, brushSize);
    },
    setBrushSize: function (canvas, brushSize) {
      const editor = getPreviewEditor(canvas);
      if (editor) {
        editor.brushSize = Number(brushSize) || editor.brushSize || 36;
      }
    },
    clearMask: function (canvas) {
      const editor = getPreviewEditor(canvas);
      if (!canvas || !editor) {
        return;
      }

      canvas.getContext('2d').clearRect(0, 0, canvas.width, canvas.height);
      editor.hasPaint = false;
      editor.lastPoint = null;
    },
    exportMask: function (canvas) {
      return exportPreviewMask(canvas);
    },
  },
};
