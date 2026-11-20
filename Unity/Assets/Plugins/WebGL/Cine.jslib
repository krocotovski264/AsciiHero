// the cinematic's frames and messages, from the game to the page (GameCine.cs): each frame's JPEG is posted to
// /frame?i=<n> on the server the page came from; the log lines and the end too
mergeInto(LibraryManager.library, {
  CineFrame: function (ptr, len, idx) {
    ptr = ptr >>> 0;                         // (past 2 GB of heap the pointer comes signed)
    var bytes = HEAPU8.slice(ptr, ptr + len);
    window.cinePending = (window.cinePending || 0) + 1;
    fetch('/frame?i=' + idx, { method: 'POST', body: bytes }).then(function () { window.cinePending--; window.cineSent = (window.cineSent || 0) + 1; });
  },
  CineLog: function (s) { var t = UTF8ToString(s); console.log(t); fetch('/log', { method: 'POST', body: t }); },
  CineDone: function (n) { window.cineDone = n; fetch('/done?n=' + n, { method: 'POST', body: '' }); }
});
