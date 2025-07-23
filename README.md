# websocketserver
用于配合 CRX 的 WebSocket 服务器，接收信息并存入 SQLite3。
运行环境：.NET 2.0 + SQLite3。

启动程序会检查 `server.pfx`，如不存在则尝试使用 `openssl` 自动生成自签名证书。
如果系统未安装 `openssl`，请根据终端提示手动创建证书。

## 安装为 Windows 服务

编译得到 `DrugInfoWssServer.exe` 后，在提升的命令行中执行：

```cmd
DrugInfoWssServer.exe install
```

卸载服务使用：

```cmd
uninstall_service.bat
```

或直接执行：

```cmd
DrugInfoWssServer.exe uninstall
```

安装后将创建名为 **DrugInfoWssServer** 的服务，启动类型为自动。服务启动后在后台监听 `wss://localhost:8443`。

## 在 CRX 中使用 WSS

CRX 插件可直接通过 `wss://localhost:8443` 与本服务通信，例如：

```javascript
const ws = new WebSocket('wss://localhost:8443');
 ws.onopen = () => {
    ws.send(JSON.stringify({action: 'query_druginfo', druginfo: {Name: '药品名称'}}));
};
ws.onmessage = evt => console.log(evt.data);
```

若需要更新某药品的生产日期，可发送：

```javascript
ws.send(JSON.stringify({
    action: 'update_druginfo_productdate',
    druginfo: {fixmedins_hilist_name: '阿莫西林胶囊', manu_date: '2025-07-23'}
}));
```

如浏览器出现证书警告，请将 `server.pfx` 导入受信任的根证书颁发机构。该证书仅用于本地开发测试。
