async (page) => {
 const checks=[],errors=[],metrics=[];
 page.on('pageerror',e=>errors.push(e.message));
 const assert=(v,m)=>{if(!v)throw Error(m);checks.push(m)};
 const key='formation-integration',headers={'X-Frost-Key':key};
 const status=async()=> (await page.request.get(new URL('/api/status',page.url()).href,{headers})).json();
 const nav=n=>page.getByRole('button',{name:n,exact:true});
 const saved=()=>page.locator('.autosave-status.saved').first().waitFor({timeout:15000});
 await page.setViewportSize({width:1440,height:1000});await nav('下潜控制').click();
 for(let i=1;i<=7;i++){
  await page.getByRole('combobox',{name:'选择下潜策略',exact:true}).click();
  await page.getByRole('option',{name:new RegExp('^策略'+['','一','二','三','四','五','六','七'][i]+' ·')}).click();
  await saved();assert((await status()).config.strategyId===i,'策略'+i+'经生产接口自动保存');
 }
 await page.getByRole('region',{name:'实战学习状态'}).waitFor();assert(true,'所有策略的实战学习面板可见');
 await nav('策略 5–7 参数').click();
 const field=page.getByRole('spinbutton',{name:'策略六搜索次数',exact:true});
 await field.fill('8192');await saved();assert((await status()).config.search.adaptiveIterations===8192,'搜索预算自动保存且不被通用参数覆盖');
 await field.fill('0');await page.locator('.autosave-status.invalid').waitFor();assert((await status()).config.search.adaptiveIterations===8192,'无效预算保留草稿，不写入有效配置');
 await field.fill('4096');await saved();
 const device=page.getByRole('combobox',{name:'神经网络训练设备',exact:true});
 assert(await device.inputValue()==='cpu','旧配置默认显示CPU训练');
 await device.selectOption('cuda');await saved();assert((await status()).config.search.trainingDevice==='cuda','CUDA选项经生产接口自动保存');
 await page.reload();await nav('策略 5–7 参数').click();assert(await device.inputValue()==='cuda','刷新恢复CUDA训练选择');
 for(const width of [1440,768,390]){
  await page.setViewportSize({width,height:1000});await field.scrollIntoViewIfNeeded();
  const metric=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth}));metrics.push(metric);
  assert(metric.scroll<=metric.width,'搜索设置在'+width+'宽度无水平溢出');
  await page.screenshot({path:'output/playwright/search/parameters-'+width+'.png',animations:'disabled'});
 }
 await nav('自定义选牌').click();
 const mode=page.getByRole('combobox',{name:'策略四选牌模式',exact:true});
 for(const [value,label] of [['family','手动系列、分类、单卡排序'],['adaptive','收益比较（默认）']]){
  await mode.selectOption(value);await saved();assert((await status()).config.customStrategy.selectionMode===value,'策略四'+label+'模式持久化');
 }
 await page.reload();await nav('自定义选牌').click();assert(await mode.inputValue()==='adaptive','刷新恢复策略四收益比较模式');
 const preset=page.getByRole('region',{name:'光系特殊继承配置'});
 assert(await preset.getByRole('button',{name:'使用此配置',exact:true}).isVisible(),'光系配置默认关闭，提供使用此配置按钮');
 const firstCard=page.locator('.custom-card').first();
 const priority=firstCard.getByRole('spinbutton').first();await priority.fill('7');await saved();
 const prior=(await status()).config.customStrategy;
 await preset.getByRole('button',{name:'使用此配置',exact:true}).click();await saved();
 const enabled=(await status()).config.customStrategy;
 assert(enabled.lightFervorOnly===true,'使用此配置保存光系开关');
 assert(JSON.stringify(enabled.cards)===JSON.stringify(prior.cards)&&JSON.stringify(enabled.familyOrder)===JSON.stringify(prior.familyOrder),'使用此配置保留单卡与系列排序');
 await page.reload();await nav('自定义选牌').click();
 assert(await preset.getByRole('button',{name:'关闭此配置',exact:true}).isVisible(),'刷新恢复已启用光系配置');
 await page.request.get(new URL('/test/state?active=1&run=1',page.url()).href);
 await page.waitForFunction(()=>document.querySelector('[aria-label="光系特殊继承配置"] button')?.disabled);
 assert(await preset.getByRole('button',{name:'关闭此配置',exact:true}).isDisabled(),'运行期间光系按钮锁定');
 await page.request.get(new URL('/test/state?active=0',page.url()).href);
 await page.waitForFunction(()=>!document.querySelector('[aria-label="光系特殊继承配置"] button')?.disabled);
 await preset.getByRole('button',{name:'关闭此配置',exact:true}).click();await saved();
 assert((await status()).config.customStrategy.lightFervorOnly===false,'暂停后关闭光系配置并保存');
 await page.getByRole('button',{name:/寒冰.*张/}).first().click();
 assert(await page.getByRole('heading',{name:'冰锥 · 遗物',exact:true}).isVisible()&&await page.getByRole('heading',{name:'冰锥 · 2张羁绊',exact:true}).isVisible(),'冰锥遗物与2张徽章明确区分');
 for(const width of [1440,768,390]){
  await page.setViewportSize({width,height:1000});await preset.scrollIntoViewIfNeeded();
  const metric=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth}));
  assert(metric.scroll<=metric.width,'光系配置在'+width+'宽度无水平溢出');
  await page.screenshot({path:'output/playwright/search/light-preset-'+width+'.png',animations:'disabled'});
 }
 await nav('策略 5–7 参数').click();
 await page.request.get(new URL('/test/state?active=1&run=1',page.url()).href);
 await page.waitForFunction(()=>document.querySelector('[aria-label="策略六搜索次数"]')?.disabled || document.querySelector('[aria-label="策略六搜索次数"]')?.closest('fieldset')?.disabled);
 assert(await field.isDisabled(),'运行中搜索与训练参数被锁定');
 assert(await device.isDisabled(),'运行中CUDA设备选项被锁定');
 await page.request.get(new URL('/test/state?active=0',page.url()).href);
 await page.waitForFunction(()=>!document.querySelector('[aria-label="策略六搜索次数"]')?.closest('fieldset')?.disabled);
 await device.selectOption('cpu');await saved();assert((await status()).config.search.trainingDevice==='cpu','暂停后可以切回CPU并保存');
 await nav('下潜控制').click();await page.getByRole('region',{name:'实战学习状态'}).scrollIntoViewIfNeeded();
 await page.screenshot({path:'output/playwright/search/control-390.png',animations:'disabled'});
 assert(errors.length===0,'浏览器无运行时异常');return {checks,metrics,errors};
}
