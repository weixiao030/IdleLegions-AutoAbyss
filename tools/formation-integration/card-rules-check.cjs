async (page) => {
 const checks=[], errors=[], metrics=[];
 page.on('pageerror',e=>errors.push(e.message));
 const assert=(v,m)=>{if(!v)throw Error(m);checks.push(m)};
 const key='formation-integration';
 const headers={'X-Frost-Key':key};
 const status=async()=> (await page.request.get(new URL('/api/status',page.url()).href,{headers})).json();
 const toggle=n=>page.getByRole('switch',{name:n,exact:true});
 const waitSaved=async(field,value)=>{await page.waitForFunction(async([field,value])=>{const r=await fetch('/api/status',{headers:{'X-Frost-Key':'formation-integration'}});return (await r.json()).config[field]===value},[field,value]);await page.locator('.autosave-status.saved').first().waitFor()};
 const settings=async()=>{await page.getByRole('button',{name:'策略参数',exact:true}).click();await page.getByRole('heading',{name:'普攻状态',exact:true}).waitFor()};
 await page.reload();await settings();
 let s=await status();
 assert(s.config.allowBasicBurn===true&&s.config.allowBasicLightning===false,'默认点燃允许、带电禁止');
 assert(await toggle('允许普攻点燃').isChecked()&&!await toggle('允许普攻带电').isChecked(),'两个独立Radix开关正确展示默认状态');
 assert(await page.locator('.basic-attack-settings .section-desc').innerText().then(t=>t.includes('引爆器一份')&&t.includes('优先不再选冰减速')&&t.includes('优先只拿冰系（含冰减速）')&&t.includes('本章已拿到冻结仍可优先多拿，等章末继承确认')),'最终规则区分继承前冰系优先与继承后避冰减速，拿到冻结不提前解除');
 for(const title of ['优先奖励的前几层','自动缔约的前几层','前X层优先不选护盾牌','跳过护盾的前几层']) {
  const field=page.locator('.field').filter({has:page.getByText(title,{exact:true})});
  assert((await field.innerText()).includes('根据强度自行配置'),title+'显示强度配置提示');
 }
 for(const [title,note] of [['每候选模拟次数','根据电脑配置自行决定'],['操作间隔','根据网络自行决定']]) {
  assert((await page.locator('.field').filter({has:page.getByText(title,{exact:true})}).innerText()).includes(note),title+'显示指定配置提示');
 }
 assert((await page.locator('.basic-attack-settings .footnote').innerText()).includes('择优兜底，不因偏好暂停'),'全部选牌策略说明兜底而非暂停');
 assert((await page.locator('.field').filter({has:page.getByText('前X层优先不选护盾牌',{exact:true})}).innerText()).includes('无合适候选可兜底拿盾'),'护盾开关明确软避让及兜底');
 assert((await page.locator('.basic-attack-settings .section-desc').innerText()).includes('冻结未确认继承前可优先多拿，不因本章已有而降权'),'冻结未继承前多拿且不降权说明可见');
 assert((await page.locator('.basic-attack-settings .section-desc').innerText()).includes('确认继承一份后不再追纯冻结，混合牌按新增弹跳和附带增益比较'),'冻结继承后停止追纯冻结、保留混合收益说明可见');
 await toggle('允许普攻点燃').uncheck();await waitSaved('allowBasicBurn',false);
 assert(!(await status()).config.allowBasicLightning,'关闭火不连动电');
 await toggle('允许普攻带电').check();await waitSaved('allowBasicLightning',true);
 assert(!(await status()).config.allowBasicBurn,'开启电不连动火');
 await page.reload();await settings();
 assert(!await toggle('允许普攻点燃').isChecked()&&await toggle('允许普攻带电').isChecked(),'反向组合刷新保留');
 const dlPromise=page.waitForEvent('download');await page.getByRole('button',{name:'导出参数',exact:true}).click();
 const dl=await dlPromise,stream=await dl.createReadStream(),chunks=[];for await(const c of stream)chunks.push(c);
 const exported=JSON.parse(Buffer.concat(chunks).toString('utf8'));
 assert(!exported.allowBasicBurn&&exported.allowBasicLightning,'参数导出包含两个独立值');
 const imported={...exported,allowBasicBurn:true,allowBasicLightning:false};
 await page.locator('input[type=file]').setInputFiles({name:'attack-rules.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(imported),'utf8')});
 await waitSaved('allowBasicBurn',true);await waitSaved('allowBasicLightning',false);
 assert(await toggle('允许普攻点燃').isChecked()&&!await toggle('允许普攻带电').isChecked(),'参数导入自动保存正确');
 await toggle('允许普攻点燃').focus();await page.keyboard.press('Space');await waitSaved('allowBasicBurn',false);
 assert(!await toggle('允许普攻点燃').isChecked(),'普攻开关可用键盘空格操作');
 await toggle('允许普攻点燃').press('Space');await waitSaved('allowBasicBurn',true);
 s=await status();const legacy={...s.config};delete legacy.allowBasicBurn;delete legacy.allowBasicLightning;
 const legacyResponse=await page.request.post(new URL('/api/config',page.url()).href,{headers,data:legacy});
 assert(legacyResponse.ok(),'生产接口接受不含新增字段的旧参数');
 s=await status();assert(s.config.allowBasicBurn&&!s.config.allowBasicLightning,'旧配置缺字段自动采用火开电关，不受旧回避开关影响');
 await page.reload();await settings();
 for(const [width,theme] of [[1440,'light'],[1440,'dark'],[390,'light'],[390,'dark']]){
  await page.setViewportSize({width,height:1000});await page.evaluate(t=>localStorage.setItem('frost-theme',t),theme);await page.reload();await settings();
  await page.locator('.basic-attack-settings').scrollIntoViewIfNeeded();
  const m=await page.evaluate(()=>({width:innerWidth,documentWidth:document.documentElement.scrollWidth}));metrics.push({...m,theme});assert(m.documentWidth<=m.width,width+' '+theme+' 无横向溢出');
  assert(await toggle('允许普攻点燃').isVisible()&&await toggle('允许普攻带电').isVisible(),width+' '+theme+' 双开关可见');
  await page.screenshot({path:'output/playwright/cards134/settings-'+width+'-'+theme+'.png',animations:'disabled'});
 }
 await page.request.get(new URL('/test/state?active=1&run=1',page.url()).href);
 await page.waitForFunction(()=>document.querySelector('[role=switch][aria-label=允许普攻点燃]')?.matches(':disabled'));
 assert(await toggle('允许普攻点燃').isDisabled()&&await toggle('允许普攻带电').isDisabled(),'运行中两个开关均锁定');
 const locked=await page.request.post(new URL('/api/config',page.url()).href,{headers,data:imported});assert(locked.status()===400,'后端同样拒绝运行中改参数');
 await page.request.get(new URL('/test/state?active=0',page.url()).href);
 await page.waitForFunction(()=>document.querySelector('[role=switch][aria-label=允许普攻点燃]')?.matches(':disabled')===false);
 assert(await toggle('允许普攻点燃').isEnabled(),'结束运行后恢复编辑');
 await page.getByRole('button',{name:'下潜控制',exact:true}).click();
 assert(await page.getByText(/契约后的第二次选择可能是徽章/).isVisible(),'控制页说明临时徽章与遗物继承差异及章末重新推演');
 assert(errors.length===0,'修复后浏览器无未处理异常');
 console.log('CARD_UI_CHECKS '+JSON.stringify({ok:true,checks,metrics,errors}));return {ok:true,checks,metrics,errors};
}
