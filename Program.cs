using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization; // 需要引用 System.Web.Extensions.dll
using System.ServiceProcess;
using System.Reflection;

namespace DrugInfoWebSocketServer
{
    // 药品信息数据结构
    public class DrugInfo
    {
        public string Name { get; set; }
        public string ManuLotnum { get; set; }
        public string ManuDate { get; set; }
        public string ExpyEnd { get; set; }
        public int KCSB { get; set; }
        public string Msg { get; set; }
        public DateTime CreateTime { get; set; }
    }

    // CRX请求消息格式
    public class CrxMessage
    {
        public string action { get; set; }
        public DrugInfo druginfo { get; set; }
    }

    // 服务器响应格式
    public class ServerResponse
    {
        public bool success { get; set; }
        public string message { get; set; }
        public object data { get; set; }
    }

    // SSL证书管理器
    public class CertificateManager
    {
        public static X509Certificate2 CreateSelfSignedCertificate(string subjectName = "localhost")
        {
            // 为.NET 2.0创建自签名证书
            // 注意：这是一个简化的实现，生产环境建议使用专业的证书生成工具
            
            string certPath = "server.pfx";
            string certPassword = "DrugInfoServer2024";
            
            if (File.Exists(certPath))
            {
                try
                {
                    return new X509Certificate2(certPath, certPassword);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("加载现有证书失败: " + ex.Message);
                }
            }
            
            // 如果证书不存在，创建一个新的
            return GenerateNewCertificate(certPath, certPassword, subjectName);
        }
        
        private static X509Certificate2 GenerateNewCertificate(string certPath, string password, string subjectName)
        {
            try
            {
                Console.WriteLine("正在自动创建SSL证书...");

                string keyFile = "tmpkey.pem";
                string certFile = "tmpcert.pem";

                int code1 = RunCommand("openssl", string.Format("req -x509 -newkey rsa:2048 -subj /CN={0} -keyout {1} -out {2} -days 365 -nodes", subjectName, keyFile, certFile));
                int code2 = RunCommand("openssl", string.Format("pkcs12 -export -out {0} -inkey {1} -in {2} -passout pass:{3}", certPath, keyFile, certFile, password));

                if (code1 != 0 || code2 != 0)
                    throw new Exception("openssl 执行失败");

                if (File.Exists(keyFile)) File.Delete(keyFile);
                if (File.Exists(certFile)) File.Delete(certFile);

                return new X509Certificate2(certPath, password);
            }
            catch (Exception ex)
            {
                Console.WriteLine("自动创建证书失败: " + ex.Message);
                Console.WriteLine("请确认已安装 openssl，或手动创建服务器证书。");
                return null;
            }
        }

        private static int RunCommand(string fileName, string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                Process p = Process.Start(psi);
                p.WaitForExit();
                return p.ExitCode;
            }
            catch
            {
                return -1;
            }
        }
        
        // 证书验证回调（用于开发环境，接受自签名证书）
        public static bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // 在开发环境中接受自签名证书
            // 生产环境应该进行严格的证书验证
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;
                
            Console.WriteLine("SSL证书验证警告: " + sslPolicyErrors);
            
            // 允许自签名证书（仅开发环境）
            if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors ||
                sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
            {
                return true;
            }
            
            return false;
        }
    }

    // SQLite数据库管理器（与之前相同）
    public class DrugInfoDatabase
    {
        private string connectionString;

        public DrugInfoDatabase(string dbPath)
        {
            connectionString = string.Format("Data Source={0};Version=3;", dbPath);
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    string createTableSql = @"
                        CREATE TABLE IF NOT EXISTS druginfo (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            name TEXT NOT NULL,
                            manu_lotnum TEXT,
                            manu_date TEXT,
                            expy_end TEXT,
                            kcsb INTEGER DEFAULT 0,
                            msg TEXT,
                            create_time DATETIME DEFAULT CURRENT_TIMESTAMP
                        )";
                    
                    using (SQLiteCommand cmd = new SQLiteCommand(createTableSql, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                    
                    Console.WriteLine("数据库初始化成功");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("数据库初始化失败: " + ex.Message);
            }
        }

        public bool InsertDrugInfo(DrugInfo drugInfo)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    string insertSql = @"
                        INSERT INTO druginfo (name, manu_lotnum, manu_date, expy_end, kcsb, msg, create_time)
                        VALUES (@name, @manu_lotnum, @manu_date, @expy_end, @kcsb, @msg, @create_time)";
                    
                    using (SQLiteCommand cmd = new SQLiteCommand(insertSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", drugInfo.Name ?? "");
                        cmd.Parameters.AddWithValue("@manu_lotnum", drugInfo.ManuLotnum ?? "");
                        cmd.Parameters.AddWithValue("@manu_date", drugInfo.ManuDate ?? "");
                        cmd.Parameters.AddWithValue("@expy_end", drugInfo.ExpyEnd ?? "");
                        cmd.Parameters.AddWithValue("@kcsb", drugInfo.KCSB);
                        cmd.Parameters.AddWithValue("@msg", drugInfo.Msg ?? "");
                        cmd.Parameters.AddWithValue("@create_time", DateTime.Now);
                        
                        int result = cmd.ExecuteNonQuery();
                        return result > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("插入数据失败: " + ex.Message);
                return false;
            }
        }

        public List<DrugInfo> GetDrugInfoByName(string name)
        {
            List<DrugInfo> result = new List<DrugInfo>();
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    string selectSql = "SELECT * FROM druginfo WHERE name LIKE @name ORDER BY create_time DESC LIMIT 10";
                    
                    using (SQLiteCommand cmd = new SQLiteCommand(selectSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", "%" + name + "%");
                        
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                DrugInfo info = new DrugInfo
                                {
                                    Name = reader["name"].ToString(),
                                    ManuLotnum = reader["manu_lotnum"].ToString(),
                                    ManuDate = reader["manu_date"].ToString(),
                                    ExpyEnd = reader["expy_end"].ToString(),
                                    KCSB = Convert.ToInt32(reader["kcsb"]),
                                    Msg = reader["msg"].ToString(),
                                    CreateTime = Convert.ToDateTime(reader["create_time"])
                                };
                                result.Add(info);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("查询数据失败: " + ex.Message);
            }
            return result;
        }

        public bool UpsertManuDate(string name, string manuDate)
        {
            if (string.IsNullOrEmpty(name)) return false;

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    // 检查记录是否存在
                    string selectSql = "SELECT COUNT(*) FROM druginfo WHERE name=@name";
                    using (SQLiteCommand selectCmd = new SQLiteCommand(selectSql, conn))
                    {
                        selectCmd.Parameters.AddWithValue("@name", name);
                        int count = Convert.ToInt32(selectCmd.ExecuteScalar());

                        if (count > 0)
                        {
                            string updateSql = "UPDATE druginfo SET manu_date=@manu_date WHERE name=@name";
                            using (SQLiteCommand updateCmd = new SQLiteCommand(updateSql, conn))
                            {
                                updateCmd.Parameters.AddWithValue("@manu_date", manuDate);
                                updateCmd.Parameters.AddWithValue("@name", name);
                                return updateCmd.ExecuteNonQuery() > 0;
                            }
                        }
                        else
                        {
                            string insertSql = "INSERT INTO druginfo (name, manu_date) VALUES (@name, @manu_date)";
                            using (SQLiteCommand insertCmd = new SQLiteCommand(insertSql, conn))
                            {
                                insertCmd.Parameters.AddWithValue("@name", name);
                                insertCmd.Parameters.AddWithValue("@manu_date", manuDate);
                                return insertCmd.ExecuteNonQuery() > 0;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("更新数据失败: " + ex.Message);
                return false;
            }
        }
    }

    // WSS WebSocket连接处理类
    public class WssWebSocketConnection
    {
        private TcpClient client;
        private SslStream sslStream;
        private bool isConnected = false;
        
        public WssWebSocketConnection(TcpClient tcpClient, X509Certificate2 serverCertificate)
        {
            client = tcpClient;
            
            // 创建SSL流
            NetworkStream networkStream = client.GetStream();
            sslStream = new SslStream(networkStream, false);
            
            try
            {
                // 进行SSL服务器身份验证
                sslStream.AuthenticateAsServer(serverCertificate, false, SslProtocols.Tls, true);
                Console.WriteLine("SSL握手成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine("SSL握手失败: " + ex.Message);
                throw;
            }
        }

        public bool PerformHandshake()
        {
            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead = sslStream.Read(buffer, 0, buffer.Length);
                string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                
                Console.WriteLine("WebSocket握手请求:");
                Console.WriteLine(request);
                
                if (request.Contains("Upgrade: websocket"))
                {
                    string key = ExtractWebSocketKey(request);
                    if (!string.IsNullOrEmpty(key))
                    {
                        string responseKey = GenerateWebSocketAcceptKey(key);
                        string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                                        "Connection: Upgrade\r\n" +
                                        "Upgrade: websocket\r\n" +
                                        "Sec-WebSocket-Accept: " + responseKey + "\r\n\r\n";
                        
                        Console.WriteLine("WebSocket握手响应:");
                        Console.WriteLine(response);
                        
                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                        sslStream.Write(responseBytes, 0, responseBytes.Length);
                        sslStream.Flush();
                        isConnected = true;
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("WebSocket握手失败: " + ex.Message);
                return false;
            }
        }

        private string ExtractWebSocketKey(string request)
        {
            Match match = Regex.Match(request, @"Sec-WebSocket-Key:\s*(.+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private string GenerateWebSocketAcceptKey(string key)
        {
            string concat = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            SHA1 sha1 = SHA1.Create();
            byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(concat));
            return Convert.ToBase64String(hash);
        }

        public string ReceiveMessage()
        {
            try
            {
                if (!isConnected) return null;
                
                byte[] buffer = new byte[2];
                int bytesRead = sslStream.Read(buffer, 0, 2);
                
                if (bytesRead < 2) return null;
                
                bool fin = (buffer[0] & 0x80) != 0;
                int opcode = buffer[0] & 0x0F;
                bool masked = (buffer[1] & 0x80) != 0;
                int payloadLength = buffer[1] & 0x7F;
                
                if (opcode == 8) // 连接关闭
                {
                    isConnected = false;
                    return null;
                }
                
                if (payloadLength == 126)
                {
                    byte[] lengthBuffer = new byte[2];
                    sslStream.Read(lengthBuffer, 0, 2);
                    payloadLength = (lengthBuffer[0] << 8) | lengthBuffer[1];
                }
                else if (payloadLength == 127)
                {
                    byte[] lengthBuffer = new byte[8];
                    sslStream.Read(lengthBuffer, 0, 8);
                    // 简化处理，不支持超大消息
                    payloadLength = (int)BitConverter.ToInt64(lengthBuffer, 0);
                }
                
                byte[] maskKey = new byte[4];
                if (masked)
                {
                    sslStream.Read(maskKey, 0, 4);
                }
                
                byte[] payload = new byte[payloadLength];
                sslStream.Read(payload, 0, payloadLength);
                
                if (masked)
                {
                    for (int i = 0; i < payload.Length; i++)
                    {
                        payload[i] ^= maskKey[i % 4];
                    }
                }
                
                return Encoding.UTF8.GetString(payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine("接收消息失败: " + ex.Message);
                isConnected = false;
                return null;
            }
        }

        public bool SendMessage(string message)
        {
            try
            {
                if (!isConnected) return false;
                
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                byte[] frame;
                
                if (messageBytes.Length < 126)
                {
                    frame = new byte[2 + messageBytes.Length];
                    frame[0] = 0x81; // FIN=1, opcode=1 (text)
                    frame[1] = (byte)messageBytes.Length;
                    Array.Copy(messageBytes, 0, frame, 2, messageBytes.Length);
                }
                else if (messageBytes.Length < 65536)
                {
                    frame = new byte[4 + messageBytes.Length];
                    frame[0] = 0x81;
                    frame[1] = 126;
                    frame[2] = (byte)(messageBytes.Length >> 8);
                    frame[3] = (byte)(messageBytes.Length & 0xFF);
                    Array.Copy(messageBytes, 0, frame, 4, messageBytes.Length);
                }
                else
                {
                    // 简化处理，不支持超大消息
                    return false;
                }
                
                sslStream.Write(frame, 0, frame.Length);
                sslStream.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("发送消息失败: " + ex.Message);
                isConnected = false;
                return false;
            }
        }

        public bool IsConnected
        {
            get { return isConnected && client.Connected; }
        }

        public void Close()
        {
            try
            {
                isConnected = false;
                if (sslStream != null) sslStream.Close();
                if (client != null) client.Close();
            }
            catch { }
        }
    }

    // 主WSS WebSocket服务器类
    public class DrugInfoWssWebSocketServer
    {
        private TcpListener server;
        private DrugInfoDatabase database;
        private JavaScriptSerializer jsonSerializer;
        private X509Certificate2 serverCertificate;
        private bool isRunning = false;
        private int port;
        private Dictionary<string, string> preloadCache = new Dictionary<string, string>();

        public DrugInfoWssWebSocketServer(int serverPort, string dbPath, X509Certificate2 certificate)
        {
            port = serverPort;
            database = new DrugInfoDatabase(dbPath);
            jsonSerializer = new JavaScriptSerializer();
            serverCertificate = certificate;
        }

        public void Start()
        {
            try
            {
                if (serverCertificate == null)
                {
                    Console.WriteLine("错误: SSL证书未找到！");
                    Console.WriteLine("请确保 server.pfx 文件存在，或手动创建SSL证书。");
                    return;
                }
                
                server = new TcpListener(IPAddress.Any, port);
                server.Start();
                isRunning = true;
                
                Console.WriteLine("药品信息WSS WebSocket服务器启动成功，监听端口: " + port);
                Console.WriteLine("SSL证书: " + serverCertificate.Subject);
                Console.WriteLine("证书有效期: " + serverCertificate.NotAfter);
                Console.WriteLine("等待CRX连接...");
                Console.WriteLine("");
                
                while (isRunning)
                {
                    TcpClient client = server.AcceptTcpClient();
                    Console.WriteLine("新的SSL客户端连接: " + client.Client.RemoteEndPoint);
                    
                    Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClient));
                    clientThread.Start(client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("WSS服务器启动失败: " + ex.Message);
            }
        }

        private void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            WssWebSocketConnection wsConnection = null;
            
            try
            {
                wsConnection = new WssWebSocketConnection(client, serverCertificate);
                
                if (wsConnection.PerformHandshake())
                {
                    Console.WriteLine("WSS WebSocket握手成功");
                    
                    while (wsConnection.IsConnected)
                    {
                        string message = wsConnection.ReceiveMessage();
                        if (message != null)
                        {
                            Console.WriteLine("收到CRX消息: " + message);
                            ProcessCrxMessage(wsConnection, message);
                        }
                        else
                        {
                            Thread.Sleep(100); // 避免CPU占用过高
                        }
                    }
                }
                
                Console.WriteLine("SSL客户端断开连接");
            }
            catch (Exception ex)
            {
                Console.WriteLine("处理SSL客户端连接异常: " + ex.Message);
            }
            finally
            {
                if (wsConnection != null) wsConnection.Close();
            }
        }

        private void ProcessCrxMessage(WssWebSocketConnection connection, string message)
        {
            try
            {
                Dictionary<string, object> msgObj = jsonSerializer.DeserializeObject(message) as Dictionary<string, object>;
                ServerResponse response = new ServerResponse();

                if (msgObj == null || !msgObj.ContainsKey("action"))
                {
                    response.success = false;
                    response.message = "消息格式错误";
                }
                else
                {
                    string action = msgObj["action"] as string;
                    switch (action)
                    {
                        case "save_druginfo":
                            CrxMessage saveMsg = jsonSerializer.Deserialize<CrxMessage>(message);
                            response = SaveDrugInfo(saveMsg.druginfo);
                            break;
                        case "query_druginfo":
                            CrxMessage queryMsg = jsonSerializer.Deserialize<CrxMessage>(message);
                            response = QueryDrugInfo(queryMsg.druginfo.Name);
                            break;
                        case "update_druginfo_preload":
                            Dictionary<string, object> infoDict = null;
                            if (msgObj.ContainsKey("druginfo"))
                                infoDict = msgObj["druginfo"] as Dictionary<string, object>;
                            response = UpdateDrugInfoPreload(infoDict);
                            break;
                        default:
                            response.success = false;
                            response.message = "未知的操作: " + action;
                            break;
                    }
                }

                string responseJson = jsonSerializer.Serialize(response);
                connection.SendMessage(responseJson);
                Console.WriteLine("发送响应: " + responseJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine("处理CRX消息异常: " + ex.Message);
                ServerResponse errorResponse = new ServerResponse
                {
                    success = false,
                    message = "服务器处理异常: " + ex.Message
                };
                string errorJson = jsonSerializer.Serialize(errorResponse);
                connection.SendMessage(errorJson);
            }
        }

        private ServerResponse UpdateDrugInfoPreload(Dictionary<string, object> info)
        {
            ServerResponse response = new ServerResponse();

            if (info == null)
            {
                response.success = false;
                response.message = "预加载数据缺失";
                return response;
            }

            string name = string.Empty;
            if (info.ContainsKey("YPMC"))
                name = info["YPMC"] as string;
            if (string.IsNullOrEmpty(name) && info.ContainsKey("fixmedins_hilist_name"))
                name = info["fixmedins_hilist_name"] as string;

            string manuDate = info.ContainsKey("manu_date") ? info["manu_date"] as string : string.Empty;

            if (!string.IsNullOrEmpty(manuDate))
            {
                preloadCache[name] = manuDate;
                bool ok = database.UpsertManuDate(name, manuDate);
                response.success = ok;
                response.message = ok ? "预加载成功" : "预加载失败";
            }
            else
            {
                response.success = false;
                response.message = "manu_date为空";
            }

            return response;
        }

        private ServerResponse SaveDrugInfo(DrugInfo drugInfo)
        {
            ServerResponse response = new ServerResponse();
            
            if (drugInfo == null || string.IsNullOrEmpty(drugInfo.Name))
            {
                response.success = false;
                response.message = "药品名称不能为空";
                return response;
            }
            
            bool success = database.InsertDrugInfo(drugInfo);
            response.success = success;
            response.message = success ? "药品信息保存成功" : "药品信息保存失败";
            
            return response;
        }

        private ServerResponse QueryDrugInfo(string drugName)
        {
            ServerResponse response = new ServerResponse();
            
            if (string.IsNullOrEmpty(drugName))
            {
                response.success = false;
                response.message = "药品名称不能为空";
                return response;
            }
            
            List<DrugInfo> drugInfoList = database.GetDrugInfoByName(drugName);
            response.success = true;
            response.message = "查询成功，找到 " + drugInfoList.Count + " 条记录";
            response.data = drugInfoList;
            
            return response;
        }

        public void Stop()
        {
            isRunning = false;
            if (server != null)
            {
                server.Stop();
            }
        }
    }

    // Windows Service wrapper
    public class DrugInfoWssService : ServiceBase
    {
        private DrugInfoWssWebSocketServer server;
        private Thread serverThread;

        public DrugInfoWssService()
        {
            this.ServiceName = "DrugInfoWssServer";
        }

        protected override void OnStart(string[] args)
        {
            int serverPort = 8443;
            string dbPath = "druginfo.db";
            X509Certificate2 certificate = CertificateManager.CreateSelfSignedCertificate("localhost");
            server = new DrugInfoWssWebSocketServer(serverPort, dbPath, certificate);
            serverThread = new Thread(new ThreadStart(server.Start));
            serverThread.Start();
        }

        protected override void OnStop()
        {
            if (server != null)
            {
                server.Stop();
            }
            if (serverThread != null && serverThread.IsAlive)
            {
                serverThread.Join(1000);
            }
        }
    }

    public static class ServiceHelper
    {
        public static void Install()
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            string args = "create \"DrugInfoWssServer\" binPath= \"" + exePath + " service\" start= auto";
            RunSc(args);
        }

        public static void Uninstall()
        {
            RunSc("delete \"DrugInfoWssServer\"");
        }

        private static void RunSc(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("sc", arguments);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Process p = Process.Start(psi);
                p.WaitForExit();
                Console.WriteLine(p.StandardOutput.ReadToEnd());
                string err = p.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(err))
                {
                    Console.WriteLine(err);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("执行SC命令失败: " + ex.Message);
            }
        }
    }

    // 程序入口
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                string cmd = args[0].ToLower();
                if (cmd == "install")
                {
                    ServiceHelper.Install();
                    return;
                }
                if (cmd == "uninstall")
                {
                    ServiceHelper.Uninstall();
                    return;
                }
            }

            if (!Environment.UserInteractive)
            {
                ServiceBase.Run(new DrugInfoWssService());
            }
            else
            {
                RunInteractive();
            }
        }

        static void RunInteractive()
        {
            Console.WriteLine("=== 药品信息WSS WebSocket服务器 ===");
            Console.WriteLine("适配.NET 2.0 + SSL/TLS + SQLite3");
            Console.WriteLine();

            int serverPort = 8443; // WSS标准端口
            string dbPath = "druginfo.db";

            try
            {
                Console.WriteLine("正在加载SSL证书...");
                X509Certificate2 certificate = CertificateManager.CreateSelfSignedCertificate("localhost");

                if (certificate == null)
                {
                    Console.WriteLine("SSL证书加载失败！请按照提示创建证书后重新运行。");
                    Console.WriteLine("按任意键退出...");
                    Console.ReadKey();
                    return;
                }

                DrugInfoWssWebSocketServer server = new DrugInfoWssWebSocketServer(serverPort, dbPath, certificate);

                Thread serverThread = new Thread(new ThreadStart(server.Start));
                serverThread.Start();

                Console.WriteLine("按 'q' 键退出服务器...");
                while (true)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                    {
                        break;
                    }
                }

                server.Stop();
                Console.WriteLine("服务器已停止");
            }
            catch (Exception ex)
            {
                Console.WriteLine("程序异常: " + ex.Message);
            }

            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }
}
