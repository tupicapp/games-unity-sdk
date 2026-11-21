// The Tupic play frame (games-core) sets window.__TUPIC_TOKEN (handed over by games-web), __TUPIC_GAME_ID and the
// Live bridge. A Unity WebGL game reads them here, the same values a web game reads.
mergeInto(LibraryManager.library, {
  TupicBridge_Get: function (namePtr) {
    var name = UTF8ToString(namePtr);
    var allowed = { token: '__TUPIC_TOKEN', gameId: '__TUPIC_GAME_ID', apiBase: '__TUPIC_API_BASE', username: '__TUPIC_USERNAME' };
    var v = allowed[name] ? (window[allowed[name]] || '') : '';
    var size = lengthBytesUTF8(v) + 1, buf = _malloc(size);
    stringToUTF8(v, buf, size);
    return buf;
  },
  TupicBridge_Language: function () {
    var v = (navigator.language || 'en');
    var size = lengthBytesUTF8(v) + 1, buf = _malloc(size);
    stringToUTF8(v, buf, size);
    return buf;
  }
});
