async (page) => {
  const checks = [], errors = [], metrics = [];
  page.on('pageerror', error => errors.push(error.message));
  const assert = (value, message) => { if (!value) throw Error(message); checks.push(message); };
  const key = 'formation-integration';
  const status = async () => (await page.request.get(new URL('/api/status', page.url()).href, {headers:{'X-Frost-Key':key}})).json();
  const items = [
    ['跳过前8次受击免疫契约','skipEightHitPact'],
    ['跳过15%闪避契约','skipDodgePact'],
    ['超时自动恢复','retryTimeouts'],
    ['前X层不选护盾牌','skipShieldCardsEarly']
  ];
  const toggle = name => page.getByRole('switch',{name,exact:true});
  const saved = () => page.locator('.autosave-status.saved').first().waitFor({timeout:15000});
  let s = await status();
  await page.request.get(new URL('/test/state?active=0',page.url()).href);
  await page.request.post(new URL('/api/config',page.url()).href,{headers:{'X-Frost-Key':key},data:{...s.config,skipEightHitPact:true,skipDodgePact:true,retryTimeouts:true,skipShieldCardsEarly:true,shieldSkipFloors:7}});
  await page.evaluate(() => localStorage.removeItem('frost-pending-config:formation-integration'));
  await page.reload();
  await page.getByRole('button',{name:'策略参数',exact:true}).click();
  s=await status();
  const floors=()=>page.locator('.field').filter({has:page.getByText('跳过护盾的前几层',{exact:true})}).getByRole('spinbutton');
  assert(s.config.shieldSkipFloors===7 && await floors().inputValue()==='7','护盾避让默认前7层');
  await floors().fill('5');await saved();
  assert((await status()).config.shieldSkipFloors===5,'护盾跳过层数修改为5并自动保存');
  for (const [name,field] of items) {
    assert(s.config[field] === true && await toggle(name).isChecked(), name+'默认开启');
    await toggle(name).uncheck(); await saved();
    s = await status();
    assert(s.config[field] === false,name+'经生产接口自动保存为关闭');
  }
  assert(s.config.autoPactWithFreeze === true,'独立避让开关不改变自动缔约主开关');
  await page.reload();
  await page.getByRole('button',{name:'策略参数',exact:true}).click();
  for (const [name] of items) assert(!await toggle(name).isChecked(),name+'刷新后恢复关闭值');
  assert(await floors().inputValue()==='5','护盾跳过层数刷新后保留5');
  const downloadPromise = page.waitForEvent('download');
  await page.getByRole('button',{name:'导出参数',exact:true}).click();
  const download = await downloadPromise;
  const stream = await download.createReadStream(); const chunks=[];
  for await (const chunk of stream) chunks.push(chunk);
  const exported = JSON.parse(Buffer.concat(chunks).toString('utf8'));
  assert(items.every(([,field])=>exported[field] === false),'导出JSON包含四个开关的当前值');
  const imported = {...exported,skipEightHitPact:true,skipDodgePact:false,retryTimeouts:true,skipShieldCardsEarly:true,shieldSkipFloors:9};
  await page.locator('input[type="file"]').setInputFiles({name:'resilience.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(imported),'utf8')});
  await saved(); s=await status();
  assert(s.config.skipEightHitPact && !s.config.skipDodgePact && s.config.retryTimeouts && s.config.skipShieldCardsEarly && s.config.shieldSkipFloors===9,'导入不同组合后自动保存，不将两个契约开关捆绑');
  for (const [width,theme] of [[1440,'light'],[390,'light'],[390,'dark']]) {
    await page.setViewportSize({width,height:1000});
    await page.evaluate(theme=>localStorage.setItem('frost-theme',theme),theme);
    await page.reload();
    await page.getByRole('button',{name:'策略参数',exact:true}).click();
    await toggle('跳过前8次受击免疫契约').scrollIntoViewIfNeeded();
    const m=await page.evaluate(()=>({viewport:innerWidth,documentWidth:document.documentElement.scrollWidth}));metrics.push(m);
    assert(m.documentWidth<=m.viewport,width+' '+theme+' 参数页无横向溢出');
    await page.screenshot({path:`output/playwright/pacts-131/settings-${width}-${theme}.png`,animations:'disabled'});
  }
  await page.request.get(new URL('/test/state?active=1&run=1',page.url()).href);
  await page.waitForFunction(()=>document.querySelector('[role="switch"][aria-label="超时自动恢复"]')?.matches(':disabled') === true);
  for (const [name] of items) assert(await toggle(name).isDisabled(),name+'运行中锁定');
  const blocked=await page.request.post(new URL('/api/config',page.url()).href,{headers:{'X-Frost-Key':key},data:imported});
  assert(blocked.status()===400,'后端也拒绝运行中修改恢复或避让选项');
  await page.request.get(new URL('/test/state?active=0',page.url()).href);
  await page.waitForFunction(()=>document.querySelector('[role="switch"][aria-label="超时自动恢复"]')?.matches(':disabled') === false);
  for (const [name] of items) await toggle(name).check();
  await floors().fill('7');await saved(); s=await status();
  assert(s.config.shieldSkipFloors===7,'护盾跳过层数恢复默认7并保存');
  assert(items.every(([,field])=>s.config[field] === true),'结束运行后可恢复默认并自动保存');
  assert(errors.length===0,'浏览器无未处理异常');
  console.log('RESILIENCE_CHECKS '+JSON.stringify({ok:true,checks,metrics,errors}));
  return {ok:true,checks,metrics,errors};
}
