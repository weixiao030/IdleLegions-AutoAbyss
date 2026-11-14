async page => {
 const checks=[],errors=[],headers={'X-Frost-Key':'formation-integration'};
 const check=(value,name)=>{if(!value)throw Error(name);checks.push(name)};
 page.on('pageerror',e=>errors.push(e.message));
 const storage=async()=>(await page.request.get(new URL('/api/storage',page.url()).href,{headers})).json();
 const before=await storage();
 check(await page.getByRole('heading',{name:'数据管理',exact:true}).isVisible(),'数据管理可从主导航打开');
 check((await page.getByRole('region',{name:'本机数据占用'}).textContent()).includes(before.usage.totalGiB.toFixed(2)+' GB'),'总占用按真实接口显示GB');
 check(await page.getByRole('checkbox',{name:'选择参数与技能备份'}).count()===0,'参数与模型没有删除入口');
 await page.getByRole('button',{name:'无损压缩历史数据',exact:true}).click();
 await page.waitForFunction(()=>document.body.textContent.includes('压缩完成：'),{},{timeout:15000});
 const packed=await storage();
 check(packed.usage.totalBytes<before.usage.totalBytes,'生产压缩接口实际减少测试档案占用');
 await page.getByRole('checkbox',{name:'选择历史战报',exact:true}).check();
 await page.getByRole('button',{name:'删除所选数据',exact:true}).click();
 const dialog=page.getByRole('alertdialog');
 check(await dialog.isVisible()&&(await dialog.textContent()).includes('历史战报'),'删除前展示具体类别和占用确认');
 await dialog.getByRole('button',{name:'取消',exact:true}).click();
 check((await storage()).usage.groups.find(g=>g.id==='battles').files===1,'取消删除保留历史文件');
 await page.getByRole('button',{name:'删除所选数据',exact:true}).click();
 await dialog.getByRole('button',{name:'确认永久删除',exact:true}).click();
 await page.waitForFunction(()=>document.body.textContent.includes('删除完成：'),{},{timeout:15000});
 const deleted=await storage();
 await page.getByRole('alertdialog').waitFor({state:'detached'});
 check(deleted.usage.groups.find(g=>g.id==='battles').files===0&&deleted.usage.groups.find(g=>g.id==='decisions').files===1,'确认后只删除所选历史，不删除未选学习记录');
 check(deleted.usage.groups.find(g=>g.id==='settings').files===1,'生产删除接口保留参数文件');
 for(const width of [1440,768,390]){
  await page.setViewportSize({width,height:1000});
  await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
  check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'数据管理'+width+'宽度无页面横向溢出');
  await page.screenshot({path:'output/playwright/storage-'+width+'.png',fullPage:true,animations:'disabled'});
 }
 const theme=page.getByRole('button',{name:'切换深色',exact:true});
 if(await theme.count())await theme.click();
 check(await page.locator('.radix-themes.dark').first().isVisible(),'数据管理支持深色模式');
 await page.screenshot({path:'output/playwright/storage-dark-390.png',fullPage:true});
 check(errors.length===0,'数据管理浏览器无运行时异常');
 return {ok:true,checks,errors,beforeBytes:before.usage.totalBytes,packedBytes:packed.usage.totalBytes};
}
