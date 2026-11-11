async (page) => {
 const checks=[],errors=[],headers={'X-Frost-Key':'formation-integration'};
 page.on('pageerror',e=>errors.push(e.message));
 const check=(v,m)=>{if(!v)throw Error(m);checks.push(m)};
 const nav=n=>page.getByRole('button',{name:n,exact:true});
 const status=async()=>(await page.request.get(new URL('/api/status',page.url()).href,{headers})).json();
 const saved=()=>page.locator('.autosave-status.saved').first().waitFor({timeout:15000});
 if(await nav('切换浅色').count()){await nav('切换浅色').click();await page.locator('.radix-themes.light').first().waitFor();}
 await nav('策略参数').click();
 check(await page.getByRole('region',{name:'搜索与训练设置'}).count()===0,'通用参数页不再重复显示搜索设置');
 await nav('策略 5–7 参数').click();
 check(await page.getByRole('heading',{name:'策略 5–7 参数',exact:true}).isVisible(),'主导航打开独立策略5至7参数页');
 const s=await status(),rec=s.searchHardware.recommendation.parameters;
 check(await page.getByRole('region',{name:'策略五至七参数说明'}).isVisible(),'独立页面列明策略5至7的参数作用与区别');
 check((await page.getByRole('status',{name:'本机配置与自动参数'}).textContent()).includes(String(s.searchHardware.logicalProcessors)),'硬件说明使用生产接口的本机线程信息');
 const mode=page.getByRole('combobox',{name:'参数配置方式',exact:true});
 const device=page.getByRole('combobox',{name:'神经网络训练设备',exact:true});
 const fields={iterations:'策略五搜索次数',adaptiveIterations:'策略六搜索次数',neuralIterations:'策略七搜索次数',horizon:'每条路径最多步数',workers:'搜索并发',trainingEpochs:'自动训练轮数',exploration:'探索系数'};
 await device.selectOption('cuda');await saved();
 await mode.selectOption('auto');await saved();
 let cfg=(await status()).config;
 check(cfg.search.mode==='auto'&&cfg.search.trainingDevice==='cuda','Auto经生产接口保存且保留CUDA选择');
 for(const [key,name] of Object.entries(fields)){
  const input=page.getByRole('spinbutton',{name,exact:true});
  check(Number(await input.inputValue())===rec[key]&&await input.isDisabled(),name+'显示主机Auto值并锁定手改');
 }
 await page.reload();await nav('策略 5–7 参数').click();
 check(await mode.inputValue()==='auto'&&await device.inputValue()==='cuda','刷新恢复Auto与独立训练设备');
 await mode.selectOption('manual');await saved();
 const iterations=page.getByRole('spinbutton',{name:'策略五搜索次数',exact:true});
 check(await iterations.isEnabled()&&Number(await iterations.inputValue())===rec.iterations,'切回手动保留Auto数值并允许编辑');
 await iterations.fill('137');await saved();
 check((await status()).config.search.iterations===137,'切回手动后的自定义数值正常保存');
 cfg=(await status()).config;cfg.search.mode='auto';cfg.search.iterations=99999;cfg.search.workers=64;
 const response=await page.request.post(new URL('/api/config',page.url()).href,{headers,data:cfg});
 const normalized=await response.json();
 check(response.ok()&&normalized.config.search.iterations===rec.iterations&&normalized.config.search.workers===rec.workers,'后端独立重新核对Auto，返回实际采用值');
 const invalid=structuredClone(cfg);invalid.search.mode='unknown';
 check(!(await page.request.post(new URL('/api/config',page.url()).href,{headers,data:invalid})).ok(),'后端拒绝未知Auto配置方式');
 await page.reload();await nav('策略 5–7 参数').click();
 check(Number(await iterations.inputValue())===rec.iterations,'服务端归一化参数刷新后正确回显');
 await page.request.get(new URL('/test/state?active=1&run=1',page.url()).href);
 await page.waitForFunction(()=>document.querySelector('#search-mode')?.disabled);
 check(await mode.isDisabled()&&await device.isDisabled(),'运行中锁定Auto模式与训练设备');
 await page.request.get(new URL('/test/state?active=0',page.url()).href);
 await page.waitForFunction(()=>!document.querySelector('#search-mode')?.disabled);
 await device.selectOption('cpu');await saved();
 check((await status()).config.search.trainingDevice==='cpu'&&(await status()).config.search.mode==='auto','Auto下可独立关闭CUDA');
 for(const width of [1440,768,390]){
  await page.setViewportSize({width,height:1000});await page.evaluate(()=>scrollTo(0,0));
  check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'独立Auto页面在'+width+'宽度无横向溢出');
  await page.screenshot({path:'output/playwright/search/auto-page-'+width+'.png',animations:'disabled'});
 }
 await nav('切换深色').click();
 await page.locator('.radix-themes.dark').first().waitFor();
 check(await page.locator('.radix-themes.dark').first().isVisible(),'独立页面支持深色模式');
 await page.screenshot({path:'output/playwright/search/auto-page-dark-390.png',animations:'disabled'});
 await nav('切换浅色').click();
 check(errors.length===0,'Auto流程浏览器无运行时异常');
 return {ok:true,checks,hardware:s.searchHardware,errors};
}
