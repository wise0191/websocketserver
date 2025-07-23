# websocketserver

该项目实现了一个简单的 WebSocket 服务器，配合 CRX 插件使用，用于接收药品信息并写入本地 SQLite 数据库。代码基于 .NET Framework 2.0 开发，启动后在 **ws://localhost:26662** 监听连接（无 SSL 加密）。

## 编译与运行

1. 使用 Visual Studio 或 `msbuild` 编译 `DrugInfoWssServer.csproj`，生成 `DrugInfoWssServer.exe`。
2. 直接运行可在控制台模式启动服务，按 `q` 键退出：

   ```cmd
   DrugInfoWssServer.exe
   ```
3. 如需安装为 Windows 服务，在提升权限的命令行执行：

   ```cmd
   DrugInfoWssServer.exe install
   ```
   卸载服务可执行 `uninstall_service.bat` 或：
   ```cmd
   DrugInfoWssServer.exe uninstall
   ```
   安装后将创建名为 **DrugInfoWsServer** 的服务，启动类型为自动。

## WebSocket 消息格式

客户端通过 `ws://localhost:26662` 建立连接，发送 JSON 消息并以 `action` 字段指明操作。例如查询历史数据：

```javascript
const ws = new WebSocket('ws://localhost:26662');
ws.onopen = () => {
    ws.send(JSON.stringify({
        action: 'query_druginfo',
        druginfo: { Name: '药品名称' }
    }));
};
ws.onmessage = evt => console.log(evt.data);
```

支持的 `action` 包括：

- `save_druginfo` 保存单条药品信息。
- `query_manu_date_by_name` 根据名称查询生产日期。
- `query_druginfo` 查询最近的药品记录。
- `update_druginfo_preload` 预加载生产日期并写库。
- `update_druginfo_batch` 批量写入药品信息。
- `update_druginfo_productdate` 更新指定药品的生产日期。

JSON 返回值均包含 `success`、`message` 和可选 `data` 字段，具体格式可参考 `Program.cs` 中的实现。

