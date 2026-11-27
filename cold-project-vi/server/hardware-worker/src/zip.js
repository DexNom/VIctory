// One file of a zip held in memory (a report's summary.txt), as text: stored or deflated entries.
// null when the zip or the entry cannot be read.
export async function zipFile(bytes, name) {
  const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let end = -1;
  for (let i = bytes.length - 22; i >= Math.max(0, bytes.length - 65557); i--) if (v.getUint32(i, true) === 0x06054b50) { end = i; break; }
  if (end < 0) return null;
  let at = v.getUint32(end + 16, true);
  const count = v.getUint16(end + 10, true);
  for (let k = 0; k < count && at + 46 <= bytes.length; k++) {
    if (v.getUint32(at, true) !== 0x02014b50) return null;
    const method = v.getUint16(at + 10, true), size = v.getUint32(at + 20, true);
    const nameLen = v.getUint16(at + 28, true), extra = v.getUint16(at + 30, true), comment = v.getUint16(at + 32, true);
    const local = v.getUint32(at + 42, true);
    const entry = new TextDecoder().decode(bytes.subarray(at + 46, at + 46 + nameLen));
    if (entry === name && local + 30 <= bytes.length && v.getUint32(local, true) === 0x04034b50) {
      const start = local + 30 + v.getUint16(local + 26, true) + v.getUint16(local + 28, true);
      const data = bytes.subarray(start, start + size);
      if (method === 0) return new TextDecoder().decode(data);
      if (method !== 8) return null;
      const out = new Response(new Blob([data]).stream().pipeThrough(new DecompressionStream('deflate-raw')));
      return new TextDecoder().decode(await out.arrayBuffer());
    }
    at += 46 + nameLen + extra + comment;
  }
  return null;
}
