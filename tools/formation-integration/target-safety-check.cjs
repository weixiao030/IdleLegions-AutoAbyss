async (page) => {
 const checks=[],errors=[],metrics=[];page.on('pageerror',e=>errors.push(e.message));
 const assert=(ok,name)=>{if(!ok)throw Error(name);checks.push(name)};
 const headers={'X-Frost-Key':'formation-integration'};
 const status=async()=>(await page.request.get(new URL('/api/status',page.url()).href,{headers})).json();
 const toggle=n=>page.getByRole('switch',{name:n,exact:true});
 const settings=async()=>{await page.getByRole('button',{name:'策略参数',exact:true}).click();await toggle('分别锁定目标').waitFor()};
 const saved=async(k,v)=>{await page.waitForFunction(async([k,v])=>{const s=await(await fetch('/api/status',{headers:{'X-Frost-Key':'formation-integration'}})).json();return JSON.stringify(s.config[k])===JSON.stringify(v)},[k,v]);await page.locator('.autosave-status.saved').first().waitFor()};
 await page.setViewportSize({width:1440,height:1000});await page.reload();await settings();
 let s=await status();
 assert(s.config.simulations===2000,'生产后端默认每候选2000次');
 assert(await page.locator('.field').filter({has:page.getByText('每候选模拟次数',{exact:true})}).getByRole('spinbutton').inputValue()==='2000','前端显示真实2000次默认');
 for(const [title,key] of [['分别锁定目标','separateTargets'],['模型假设：纯电伤清除冻结','lightningConsumesFreeze'],['允许精英路线','allowElite'],['允许骨库伏击','allowAmbush']])assert(s.config[key]===true&&await toggle(title).isChecked(),title+'默认开启且前后端一致');
 assert(s.config.avoidEliteIds.length===1&&s.config.avoidEliteIds[0]==='sg_alferez','默认名单仅陪葬旗官真实ID');
 await page.getByRole('button',{name:'优先跳过的精英怪',exact:true}).click();
 assert(await page.getByRole('checkbox',{name:'陪葬旗官',exact:true}).isChecked(),'精英列表默认陪葬旗官打勾');await page.keyboard.press('Escape');
 assert((await page.locator('.field').filter({has:page.getByText('分别锁定目标',{exact:true})}).innerText()).includes('敌人较少时均衡复用'),'分别锁定说明可见，不伪装成全员集火');
 assert((await page.locator('.field').filter({has:page.getByText('模型假设：纯电伤清除冻结',{exact:true})}).innerText()).includes('服务端规则尚未确认'),'纯电清冻结明确仍为模型假设');
 await toggle('分别锁定目标').uncheck();await saved('separateTargets',false);await page.reload();await settings();assert(!await toggle('分别锁定目标').isChecked(),'分别锁定可关闭并持久化');
 await toggle('模型假设：纯电伤清除冻结').uncheck();await saved('lightningConsumesFreeze',false);assert(!(await status()).config.allowBasicLightning,'模型假设开关不联动普攻带电');
 const download=page.waitForEvent('download');await page.getByRole('button',{name:'导出参数',exact:true}).click();const dl=await download,stream=await dl.createReadStream(),chunks=[];for await(const c of stream)chunks.push(c);const exported=JSON.parse(Buffer.concat(chunks).toString('utf8'));
 assert(exported.separateTargets===false&&exported.lightningConsumesFreeze===false,'导出包含新增开关和显式false');
 const imported={...exported,separateTargets:true,lightningConsumesFreeze:true,allowElite:true,allowAmbush:true,avoidEliteIds:['sg_alferez'],simulations:2000};await page.locator('input[type=file]').setInputFiles({name:'target-safety.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(imported),'utf8')});await saved('separateTargets',true);await saved('lightningConsumesFreeze',true);assert(await toggle('分别锁定目标').isChecked(),'导入新字段自动保存');
 const legacy={...imported};delete legacy.separateTargets;const response=await page.request.post(new URL('/api/config',page.url()).href,{headers,data:legacy});assert(response.ok()&&(await status()).config.separateTargets,'生产接口旧配置缺新增字段默认开启');
 for(const [width,theme] of [[1440,'light'],[1440,'dark'],[390,'light'],[390,'dark']]){
  await page.setViewportSize({width,height:1000});await page.evaluate(t=>localStorage.setItem('frost-theme',t),theme);await page.reload();await settings();await toggle('分别锁定目标').scrollIntoViewIfNeeded();const m=await page.evaluate(()=>({width:innerWidth,documentWidth:document.documentElement.scrollWidth}));metrics.push({...m,theme});assert(m.documentWidth<=m.width,width+' '+theme+'无横向溢出');assert(await toggle('分别锁定目标').isVisible(),width+' '+theme+'新开关可见');await page.screenshot({path:'output/goal135/browser/targets-'+width+'-'+theme+'.png',animations:'disabled'});
 }
 await page.request.get(new URL('/test/state?active=1&run=1',page.url()).href);await page.waitForFunction(()=>document.querySelector('[role=switch][aria-label=分别锁定目标]')?.matches(':disabled'));assert(await toggle('分别锁定目标').isDisabled(),'运行中禁止修改分别锁定');
 await page.request.get(new URL('/test/state?active=0',page.url()).href);await page.waitForFunction(()=>!document.querySelector('[role=switch][aria-label=分别锁定目标]')?.matches(':disabled'));await toggle('分别锁定目标').focus();await page.keyboard.press('Space');await saved('separateTargets',false);assert(!await toggle('分别锁定目标').isChecked(),'键盘空格可关闭分别锁定');await page.keyboard.press('Space');await saved('separateTargets',true);
 assert(errors.length===0,'新增设置无浏览器未处理异常');return {ok:true,checks,metrics,errors};
}