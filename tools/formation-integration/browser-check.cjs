async (page) => {
  const checks = [], errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const assert = (value, message) => { if (!value) throw Error(message); checks.push(message); };
  const positions = ['前排 1 号位', '前排 2 号位', '后排 1 号位', '后排 2 号位'];
  const professions = ['狂战士', '弓箭手', '治疗师', '刺客', '圣骑士', '瘟疫医生', '法师', '十字军'];
  const defaultNames = ['狂战士', '法师', '弓箭手', '圣骑士'];
  const key = 'formation-integration';
  const status = async () => (await page.request.get(new URL('/api/status', page.url()).href, {headers: {'X-Frost-Key': key}})).json();
  const selected = async () => Promise.all(positions.map(name => page.getByRole('combobox', {name, exact: true}).innerText()));
  const expectTeam = async (team, message) => {
    await page.waitForFunction(team => JSON.stringify([...document.querySelectorAll('.formation-slot [role="combobox"]')].map(c => c.innerText)) === JSON.stringify(team), team);
    assert(JSON.stringify(await selected()) === JSON.stringify(team), message);
  };
  const choose = async (index, name) => {
    await page.getByRole('combobox', {name: positions[index], exact: true}).click();
    await page.getByRole('option', {name, exact: true}).click();
    await page.getByRole('listbox').waitFor({state: 'hidden'});
    await page.waitForFunction(({index, name}) => document.querySelectorAll('.formation-slot [role="combobox"]')[index]?.innerText === name, {index, name});
  };
  const reset = async () => {
    const button = page.getByRole('button', {name: '恢复默认', exact: true});
    if (await button.isEnabled()) await button.click();
  };
  await page.setViewportSize({width: 1440, height: 1000});
  await page.keyboard.press('Escape');
  await page.getByRole('button', {name: '策略参数', exact: true}).click();
  await reset();
  await expectTeam(defaultNames, '首次打开使用图二默认顺序');
  for (let i = 0; i < positions.length; i++) {
    await page.getByRole('combobox', {name: positions[i], exact: true}).click();
    const options = await page.getByRole('option').allTextContents();
    assert(JSON.stringify(options) === JSON.stringify(professions), `${positions[i]}列出全部八个中文职业`);
    await page.keyboard.press('Escape');
    for (const name of professions) {
      await choose(i, name);
      const team = await selected();
      if (team[i] !== name || new Set(team).size !== 4) throw Error(`选择失败：${positions[i]} ${name}`);
    }
  }
  checks.push('每个位置可选全部职业，32种选择均保持四位不重复');
  await reset();
  await choose(0, '圣骑士');
  await expectTeam(['圣骑士', '法师', '弓箭手', '狂战士'], '已上阵职业跨前后排交换，取消首尾职业限制');
  await page.getByRole('button', {name: '前排 2 号位后移', exact: true}).click();
  await expectTeam(['圣骑士', '弓箭手', '法师', '狂战士'], '后移可跨前排后排边界');
  await page.getByRole('button', {name: '后排 1 号位前移', exact: true}).click();
  await expectTeam(['圣骑士', '法师', '弓箭手', '狂战士'], '前移可跨前排后排边界');
  assert(await page.getByRole('button', {name: '前排 1 号位前移', exact: true}).isDisabled() && await page.getByRole('button', {name: '后排 2 号位后移', exact: true}).isDisabled(), '首尾移动按钮正确禁用');
  await reset();
  const simulations = page.getByRole('spinbutton', {name: '每候选模拟次数 2,000～50,000', exact: true});
  await simulations.fill('6144');
  await choose(1, '刺客');
  await choose(2, '法师');
  await expectTeam(['狂战士', '刺客', '法师', '圣骑士'], '可以选择用户示例阵容');
  await page.getByRole('button', {name: '保存站位', exact: true}).click();
  await page.getByText('站位已保存，下次入场生效。', {exact: true}).waitFor();
  let s = await status();
  assert(JSON.stringify(s.config.formation) === JSON.stringify(['berserker', 'asesino', 'mago', 'paladin']), '点击保存站位将所选顺序写入真实生产接口');
  assert(s.config.simulations === 4096 && await simulations.inputValue() === '6144' && await page.getByRole('button', {name: '保存参数', exact: true}).isEnabled(), '单独保存站位保留其他未保存草稿，不覆盖后端其他参数');
  await page.reload();
  await page.getByRole('button', {name: '策略参数', exact: true}).click();
  await expectTeam(['狂战士', '刺客', '法师', '圣骑士'], '刷新页面恢复已保存的自定义站位');
  assert(await simulations.inputValue() === '4096', '未保存的其他草稿不会意外落盘');
  const invalid = await page.request.post(new URL('/api/formation', page.url()).href, {headers: {'X-Frost-Key': key}, data: {formation: ['mago', 'mago', 'healer', 'cruzado']}});
  assert(invalid.status() === 400 && JSON.stringify((await status()).config.formation) === JSON.stringify(s.config.formation), 'HTTP接口拒绝重复职业且保留上次保存');
  await reset();
  await page.getByRole('button', {name: '保存参数', exact: true}).click();
  await page.getByText('参数已保存。', {exact: true}).waitFor();
  assert(JSON.stringify((await status()).config.formation) === JSON.stringify(['berserker', 'mago', 'arquera', 'paladin']), '保存全部参数也能保存恢复后的默认站位');
  // Capture both themes and all shipped layout classes in the same inspection.
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({path: 'output/formation/desktop-light.png', animations: 'disabled'});
  const metrics = [];
  for (const width of [1440, 1024, 768, 390]) {
    await page.setViewportSize({width, height: 1000});
    metrics.push(await page.locator('.formation-editor').evaluate((element, width) => {
      const rect = element.getBoundingClientRect();
      return {width, documentWidth: document.documentElement.scrollWidth, viewport: innerWidth, left: rect.left, right: rect.right, controls: [...element.querySelectorAll('[role="combobox"]')].map(c => ({width: c.clientWidth, overflow: c.scrollWidth > c.clientWidth}))};
    }, width));
  }
  assert(metrics.every(m => m.documentWidth <= m.viewport && m.left >= 0 && m.right <= m.viewport && m.controls.every(c => !c.overflow)), '1440/1024/768/390宽度均无页面或职业下拉框溢出');
  await page.getByRole('combobox', {name: positions[0], exact: true}).click();
  await page.getByRole('option', {name: '十字军', exact: true}).waitFor();
  await page.screenshot({path: 'output/formation/mobile-menu-light.png', animations: 'disabled'});
  await page.keyboard.press('Escape');
  await page.evaluate(() => {localStorage.setItem('frost-theme', 'dark');});
  await page.reload();
  await page.getByRole('button', {name: '策略参数', exact: true}).click();
  await page.screenshot({path: 'output/formation/mobile-dark.png', animations: 'disabled'});
  await choose(0, '瘟疫医生');
  await page.route('**/api/formation', route => route.fulfill({status: 400, contentType: 'application/json', body: JSON.stringify({ok: false, error: '测试保存失败，请重试。'})}));
  await page.getByRole('button', {name: '保存站位', exact: true}).click();
  await page.getByRole('alert').filter({hasText: '测试保存失败，请重试。'}).waitFor();
  assert((await selected())[0] === '瘟疫医生' && await page.getByRole('button', {name: '保存站位', exact: true}).isEnabled(), '保存失败明确显示错误，保留所选职业并允许重试');
  await page.unroute('**/api/formation');
  await page.request.get(new URL('/test/state?active=1&run=1', page.url()).href);
  await page.getByRole('combobox', {name: positions[0], exact: true}).waitFor({state: 'visible'});
  await page.waitForFunction(() => document.querySelector('[aria-label="前排 1 号位"]')?.disabled === true);
  assert(await page.getByRole('button', {name: '保存站位', exact: true}).isDisabled(), '运行时禁用职业及保存按钮');
  assert((await page.locator('.formation-editor').innerText()).includes('当前深渊已入场，保存后将在下一次入场使用。'), '已有深渊明确提示下次入场生效');
  const duringRun = await page.request.post(new URL('/api/formation', page.url()).href, {headers: {'X-Frost-Key': key}, data: {formation: ['healer', 'asesino', 'medico', 'cruzado']}});
  assert(duringRun.status() === 400 && (await duringRun.json()).error.includes('暂停'), '后端运行中也拒绝修改站位');
  await page.request.get(new URL('/test/state?active=0', page.url()).href);
  await page.waitForFunction(() => document.querySelector('[aria-label="前排 1 号位"]')?.disabled === false);
  await reset();
  await page.setViewportSize({width: 1440, height: 1000});
  const closeError = page.getByRole('button', {name: '关闭错误', exact: true});
  if (await closeError.isVisible()) await closeError.click();
  await choose(1, '刺客');
  await choose(2, '法师');
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({path: 'output/formation/desktop-dark.png', animations: 'disabled'});
  assert(errors.length === 0, '浏览器无未处理异常');
  return {ok: true, checks, metrics, errors};
}
