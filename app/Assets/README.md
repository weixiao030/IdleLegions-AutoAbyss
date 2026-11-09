# 冰晶下潜图标

深色井口底色、青白冰晶和青绿向下箭头，与控制台品牌配色一致。未使用游戏美术。

- `app.svg`：可缩放源图；字形来自 Phosphor Icons 的 `Snowflake`、`CaretDown`，bold，原始路径未改动。
- `app.png`：512 × 512 透明背景预览。
- `app.ico`：16、20、24、32、40、48、64、128、256 像素，32 位 RGBA；每档从矢量单独渲染，适用于 Windows 10/11。
- `ui/public/favicon.ico`：与 EXE 和托盘相同的多尺寸图标。

常规构建直接使用已提交资源，无需重新生成。要修改品牌资源，先安装 `ui/package-lock.json` 中的依赖，并确保 Node 可解析 `sharp`（可通过 `NODE_PATH` 指定），再运行 `node tools/generate_icon.cjs`。不需要联网生成图片。

Phosphor 字形遵循 MIT 许可；完整声明见 `docs/第三方组件.txt` 中 `@phosphor-icons/react 2.1.10` 段落。构图和其余项目代码使用项目 Apache-2.0 许可。
