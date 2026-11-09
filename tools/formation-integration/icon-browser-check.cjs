async (page) => {
  const checks = [];
  const assert = (condition, message) => { if (!condition) throw Error(message); checks.push(message); };
  await page.reload();
  const link = page.locator('link[rel="icon"]');
  assert(await link.count() === 1 && await link.getAttribute('href') === '/favicon.ico', '生产页面仅声明一个本机favicon');
  const response = await page.request.get(new URL('/favicon.ico', page.url()).href);
  assert(response.status() === 200, 'favicon通过真实生产HTTP入口返回200，而非旧204');
  assert(response.headers()['content-type'] === 'image/x-icon', 'favicon使用正确的ICO媒体类型');
  const bytes = await response.body();
  assert(bytes.readUInt16LE(2) === 1 && bytes.readUInt16LE(4) === 9, 'HTTP图标为九档多尺寸ICO');
  const sizes = Array.from({length: 9}, (_, i) => bytes[6 + i * 16] || 256);
  assert(JSON.stringify(sizes) === JSON.stringify([16,20,24,32,40,48,64,128,256]), '完整传输16至256像素图标');
  const decoded = await page.evaluate(async () => {
    const img = new Image(); img.src = document.querySelector('link[rel="icon"]').href;
    await img.decode();
    const canvas = document.createElement('canvas'); canvas.width = canvas.height = 256;
    const context = canvas.getContext('2d'); context.drawImage(img, 0, 0, 256, 256);
    const rgba = context.getImageData(0,0,256,256).data;
    return {width: img.naturalWidth, height: img.naturalHeight, visible: rgba.filter((value, i) => i % 4 === 3 && value > 0).length};
  });
  assert(decoded.width === 256 && decoded.height === 256 && decoded.visible > 45000, 'Chromium实际解码图标成功且非空白');
  const status = await page.request.get(new URL('/api/status', page.url()).href, {headers: {'X-Frost-Key': 'formation-integration'}});
  const body = await status.json();
  assert(body.config.formation.join(',') === 'berserker,mago,arquera,paladin' && body.config.avoidEliteIds.length === 0, '图标更新不改变默认阵容与默认空精英名单');
  return {ok: true, checks, decoded};
}
