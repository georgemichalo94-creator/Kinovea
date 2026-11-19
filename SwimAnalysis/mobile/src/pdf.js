// A one-page PDF (A4) that carries the report as a picture: written by hand, so that the page needs no library and
// the Greek text is exactly what was drawn.
function swimPdfFactory() {
  'use strict';

  function ascii(s) {
    var out = new Uint8Array(s.length), i;
    for (i = 0; i < s.length; i++) out[i] = s.charCodeAt(i) & 0xFF;
    return out;
  }

  /** A text of any language as a PDF string (UTF-16BE in hex). */
  function hexText(s) {
    var out = 'FEFF', i, c;
    for (i = 0; i < s.length; i++) {
      c = s.charCodeAt(i).toString(16).toUpperCase();
      out += '0000'.slice(c.length) + c;
    }
    return '<' + out + '>';
  }

  /**
   * jpeg: Uint8Array of a JPEG (as a canvas writes it), width × height its pixels, title the name of the document.
   * The picture fills an A4 page (the canvas of the report has its proportions).
   */
  function fromJpeg(jpeg, width, height, title) {
    var W = 595.28, H = 841.89, scale = Math.min(W / width, H / height);
    var w = (width * scale).toFixed(2), h = (height * scale).toFixed(2);
    var x = ((W - width * scale) / 2).toFixed(2), y = ((H - height * scale) / 2).toFixed(2);
    var content = 'q ' + w + ' 0 0 ' + h + ' ' + x + ' ' + y + ' cm /Im0 Do Q';
    var parts = [], offsets = [], length = 0;
    function push(bytes) { parts.push(bytes); length += bytes.length; }
    function object(n, body) { offsets[n] = length; push(ascii(n + ' 0 obj\n' + body + '\nendobj\n')); }

    push(ascii('%PDF-1.4\n%\xE2\xE3\xCF\xD3\n'));
    object(1, '<< /Type /Catalog /Pages 2 0 R >>');
    object(2, '<< /Type /Pages /Kids [3 0 R] /Count 1 >>');
    object(3, '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ' + W + ' ' + H + '] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>');
    offsets[4] = length;
    push(ascii('4 0 obj\n<< /Type /XObject /Subtype /Image /Width ' + width + ' /Height ' + height +
      ' /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ' + jpeg.length + ' >>\nstream\n'));
    push(jpeg);
    push(ascii('\nendstream\nendobj\n'));
    object(5, '<< /Length ' + content.length + ' >>\nstream\n' + content + '\nendstream');
    object(6, '<< /Title ' + hexText(title || '') + ' /Producer (Kinovea Swim) >>');

    var xref = length, table = 'xref\n0 7\n0000000000 65535 f \n', n, o;
    for (n = 1; n <= 6; n++) {
      o = String(offsets[n]);
      table += '0000000000'.slice(o.length) + o + ' 00000 n \n';
    }
    push(ascii(table + 'trailer\n<< /Size 7 /Root 1 0 R /Info 6 0 R >>\nstartxref\n' + xref + '\n%%EOF\n'));

    var out = new Uint8Array(length), at = 0;
    parts.forEach(function (p) { out.set(p, at); at += p.length; });
    return out;
  }

  return { fromJpeg: fromJpeg };
}

if (typeof module !== 'undefined' && module.exports) module.exports = swimPdfFactory;
