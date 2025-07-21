# websocketserver
用于配合crx的websocketserver，接收信息，并存入sqlite3.
运行环境：.net2.0 sqlite3.

启动程序会检查 `server.pfx`，如不存在则尝试使用 `openssl` 自动生成自签名证书。
如果系统未安装 `openssl`，请根据终端提示手动创建证书。
