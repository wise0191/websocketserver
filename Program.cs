using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MiniJSON;
using System.ServiceProcess;
using System.Reflection;
using System.Security.Principal;

namespace DrugInfoWebSocketServer
{
    // 药品信息数据结构
    public class DrugInfo
    {
        [JsonAlias("YPMC", "fixmedins_hilist_name", "name")]
        public string Name { get; set; }

        [JsonAlias("manu_lotnum")]
        public string ManuLotnum { get; set; }

        [JsonAlias("manu_date")]
        public string ManuDate { get; set; }

        [JsonAlias("expy_end")]
        public string ExpyEnd { get; set; }

        [JsonAlias("YMMC")]
        public string YMMC { get; set; }

        [JsonAlias("kcsb")]
        public int KCSB { get; set; }

        [JsonAlias("msg")]
        public string Msg { get; set; }

        [JsonAlias("create_time")]
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

    // SQLite数据库管理器
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
                            YMMC TEXT,
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
                        INSERT INTO druginfo (name, manu_lotnum, manu_date, expy_end, YMMC, kcsb, msg, create_time)
                        VALUES (@name, @manu_lotnum, @manu_date, @expy_end, @YMMC, @kcsb, @msg, @create_time)";

                    using (SQLiteCommand cmd = new SQLiteCommand(insertSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", drugInfo.Name ?? "");
                        cmd.Parameters.AddWithValue("@manu_lotnum", drugInfo.ManuLotnum ?? "");
                        cmd.Parameters.AddWithValue("@manu_date", drugInfo.ManuDate ?? "");
                        cmd.Parameters.AddWithValue("@expy_end", drugInfo.ExpyEnd ?? "");
                        cmd.Parameters.AddWithValue("@YMMC", drugInfo.YMMC ?? "");
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
                                    YMMC = reader["YMMC"].ToString(),
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
                            string insertSql = "INSERT INTO druginfo (name, manu_date, YMMC) VALUES (@name, @manu_date, @YMMC)";
                            using (SQLiteCommand insertCmd = new SQLiteCommand(insertSql, conn))
                            {
                                insertCmd.Parameters.AddWithValue("@name", name);
                                insertCmd.Parameters.AddWithValue("@manu_date", manuDate);
                                insertCmd.Parameters.AddWithValue("@YMMC", name);
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

    // WS WebSocket连接处理类（移除SSL支持）
    public class WsWebSocketConnection
    {
        private TcpClient client;
        private NetworkStream networkStream;
        private bool isConnected = false;

        public WsWebSocketConnection(TcpClient tcpClient)
        {
            client = tcpClient;
            networkStream = client.GetStream();
            Console.WriteLine("WebSocket连接已建立");
        }

        public bool PerformHandshake()
        {
            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead = networkStream.Read(buffer, 0, buffer.Length);
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
                        networkStream.Write(responseBytes, 0, responseBytes.Length);
                        networkStream.Flush();
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
                int bytesRead = networkStream.Read(buffer, 0, 2);

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
                    networkStream.Read(lengthBuffer, 0, 2);
                    payloadLength = (lengthBuffer[0] << 8) | lengthBuffer[1];
                }
                else if (payloadLength == 127)
                {
                    byte[] lengthBuffer = new byte[8];
                    networkStream.Read(lengthBuffer, 0, 8);
                    // 简化处理，不支持超大消息
                    payloadLength = (int)BitConverter.ToInt64(lengthBuffer, 0);
                }

                byte[] maskKey = new byte[4];
                if (masked)
                {
                    networkStream.Read(maskKey, 0, 4);
                }

                byte[] payload = new byte[payloadLength];
                networkStream.Read(payload, 0, payloadLength);

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

                networkStream.Write(frame, 0, frame.Length);
                networkStream.Flush();
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
                if (networkStream != null) networkStream.Close();
                if (client != null) client.Close();
            }
            catch { }
        }
    }

    // 主WS WebSocket服务器类（移除SSL支持）
    public class DrugInfoWsWebSocketServer
    {
        private TcpListener server;
        private DrugInfoDatabase database;
        private SimpleJsonSerializer jsonSerializer;
        private bool isRunning = false;
        private int port;
        private Dictionary<string, string> preloadCache = new Dictionary<string, string>();

        public DrugInfoWsWebSocketServer(int serverPort, string dbPath)
        {
            port = serverPort;
            database = new DrugInfoDatabase(dbPath);
            jsonSerializer = new SimpleJsonSerializer();
        }

        public void Start()
        {
            try
            {
                server = new TcpListener(IPAddress.Any, port);
                server.Start();
                isRunning = true;

                Console.WriteLine("药品信息WS WebSocket服务器启动成功，监听端口: " + port);
                Console.WriteLine("等待CRX连接...");
                Console.WriteLine("");

                while (isRunning)
                {
                    TcpClient client = server.AcceptTcpClient();
                    Console.WriteLine("新的客户端连接: " + client.Client.RemoteEndPoint);

                    Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClient));
                    clientThread.Start(client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("WS服务器启动失败: " + ex.Message);
            }
        }

        private void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            WsWebSocketConnection wsConnection = null;

            try
            {
                wsConnection = new WsWebSocketConnection(client);

                if (wsConnection.PerformHandshake())
                {
                    Console.WriteLine("WS WebSocket握手成功");

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

                Console.WriteLine("客户端断开连接");
            }
            catch (Exception ex)
            {
                Console.WriteLine("处理客户端连接异常: " + ex.Message);
            }
            finally
            {
                if (wsConnection != null) wsConnection.Close();
            }
        }

        private void ProcessCrxMessage(WsWebSocketConnection connection, string message)
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
                        case "query_manu_date_by_name":
                            Dictionary<string, object> manuInfo = null;
                            if (msgObj.ContainsKey("data"))
                                manuInfo = msgObj["data"] as Dictionary<string, object>;
                            string reqId = msgObj.ContainsKey("requestId") ? msgObj["requestId"] as string : string.Empty;
                            var manuResp = QueryManuDateByName(manuInfo, reqId);
                            string manuJson = jsonSerializer.Serialize(manuResp);
                            connection.SendMessage(manuJson);
                            Console.WriteLine("发送响应: " + manuJson);
                            return;
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
                        case "update_druginfo_batch":
                            Dictionary<string, object> batchInfo = null;
                            if (msgObj.ContainsKey("druginfo"))
                                batchInfo = msgObj["druginfo"] as Dictionary<string, object>;
                            response = UpdateDrugInfoBatch(batchInfo);
                            break;
                        case "update_druginfo_productdate":
                            CrxMessage pdMsg = jsonSerializer.Deserialize<CrxMessage>(message);
                            response = UpdateDrugInfoProductDate(pdMsg.druginfo);
                            break;
                        case "get_druginfo_productdates":
                            List<object> namesList = null;
                            if (msgObj.ContainsKey("drugNames"))
                                namesList = msgObj["drugNames"] as List<object>;
                            string reqId2 = msgObj.ContainsKey("requestId") ? msgObj["requestId"] as string : string.Empty;
                            var datesResp = GetDrugInfoProductDates(namesList, reqId2);
                            string datesJson = jsonSerializer.Serialize(datesResp);
                            connection.SendMessage(datesJson);
                            Console.WriteLine("发送响应: " + datesJson);
                            return;
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

        private ServerResponse UpdateDrugInfoBatch(Dictionary<string, object> info)
        {
            ServerResponse response = new ServerResponse();

            if (info == null)
            {
                response.success = false;
                response.message = "批量数据缺失";
                return response;
            }

            DrugInfo di = new DrugInfo();
            if (info.ContainsKey("fixmedins_hilist_name"))
                di.Name = info["fixmedins_hilist_name"] as string;
            if (string.IsNullOrEmpty(di.Name) && info.ContainsKey("YPMC"))
                di.Name = info["YPMC"] as string;

            di.YMMC = di.Name;
            di.ManuLotnum = info.ContainsKey("manu_lotnum") ? info["manu_lotnum"] as string : string.Empty;
            di.ManuDate = info.ContainsKey("manu_date") ? info["manu_date"] as string : string.Empty;
            di.ExpyEnd = info.ContainsKey("expy_end") ? info["expy_end"] as string : string.Empty;
            if (info.ContainsKey("KCSB"))
            {
                try { di.KCSB = Convert.ToInt32(info["KCSB"]); }
                catch { di.KCSB = 0; }
            }

            bool ok = database.InsertDrugInfo(di);
            response.success = ok;
            response.message = ok ? "批量更新成功" : "批量更新失败";

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

        private ServerResponse UpdateDrugInfoProductDate(DrugInfo drugInfo)
        {
            ServerResponse response = new ServerResponse();

            if (drugInfo == null || string.IsNullOrEmpty(drugInfo.Name))
            {
                response.success = false;
                response.message = "药品名称不能为空";
                return response;
            }

            if (string.IsNullOrEmpty(drugInfo.ManuDate))
            {
                response.success = false;
                response.message = "生产日期不能为空";
                return response;
            }

            preloadCache[drugInfo.Name] = drugInfo.ManuDate;
            bool ok = database.UpsertManuDate(drugInfo.Name, drugInfo.ManuDate);
            response.success = ok;
            response.message = ok ? "更新成功" : "更新失败";

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

        private Dictionary<string, object> QueryManuDateByName(Dictionary<string, object> info, string requestId)
        {
            string name = string.Empty;
            if (info != null && info.ContainsKey("YPMC"))
                name = info["YPMC"] as string;

            string manuDate = null;
            if (!string.IsNullOrEmpty(name))
            {
                if (preloadCache.ContainsKey(name))
                {
                    manuDate = preloadCache[name];
                }
                else
                {
                    List<DrugInfo> list = database.GetDrugInfoByName(name);
                    foreach (DrugInfo di in list)
                    {
                        if (!string.IsNullOrEmpty(di.ManuDate))
                        {
                            manuDate = di.ManuDate;
                            break;
                        }
                    }
                }
            }

            Dictionary<string, object> resp = new Dictionary<string, object>();
            resp["action"] = "query_manu_date_response";
            resp["requestId"] = requestId;

            if (!string.IsNullOrEmpty(manuDate))
            {
                resp["success"] = true;
                Dictionary<string, object> data = new Dictionary<string, object>();
                data["manu_date"] = manuDate;
                data["YPMC"] = name;
                resp["data"] = data;
                resp["message"] = "查询成功";
            }
            else
            {
                resp["success"] = false;
                resp["data"] = null;
                resp["message"] = "未找到对应的药品生产日期";
            }

            return resp;
        }

        private Dictionary<string, object> GetDrugInfoProductDates(List<object> names, string requestId)
        {
            Dictionary<string, object> resp = new Dictionary<string, object>();
            resp["action"] = "get_druginfo_productdates_response";
            resp["requestId"] = requestId;

            List<Dictionary<string, object>> dateList = new List<Dictionary<string, object>>();

            if (names != null)
            {
                foreach (object obj in names)
                {
                    string name = obj == null ? string.Empty : obj.ToString();
                    string manuDate = null;

                    if (!string.IsNullOrEmpty(name))
                    {
                        if (preloadCache.ContainsKey(name))
                        {
                            manuDate = preloadCache[name];
                        }
                        else
                        {
                            List<DrugInfo> list = database.GetDrugInfoByName(name);
                            foreach (DrugInfo di in list)
                            {
                                if (!string.IsNullOrEmpty(di.ManuDate))
                                {
                                    manuDate = di.ManuDate;
                                    break;
                                }
                            }
                        }
                    }

                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["drugName"] = name;
                    item["fixmedins_hilist_name"] = name;
                    item["manu_date"] = manuDate;
                    dateList.Add(item);
                }
            }

            resp["drugDates"] = dateList;
            resp["success"] = true;
            resp["message"] = "查询完成";

            return resp;
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
    public class DrugInfoWsService : ServiceBase
    {
        private DrugInfoWsWebSocketServer server;
        private Thread serverThread;

        public DrugInfoWsService()
        {
            this.ServiceName = "DrugInfoWsServer";
        }

        protected override void OnStart(string[] args)
        {
            int serverPort = 26662; // WS端口（非SSL）
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string dbPath = Path.Combine(exeDir, "druginfo.db");
            Program.EnsureDatabase(dbPath);
            server = new DrugInfoWsWebSocketServer(serverPort, dbPath);
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
            string args = "create \"DrugInfoWsServer\" binPath= \"" + exePath + " service\" start= auto";
            RunSc(args);
        }

        public static void Uninstall()
        {
            RunSc("delete \"DrugInfoWsServer\"");
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
        private static Mutex instanceMutex;

        private static bool EnsureSingleInstance()
        {
            try
            {
                int currentId = Process.GetCurrentProcess().Id;
                string procName = Process.GetCurrentProcess().ProcessName;
                foreach (Process p in Process.GetProcessesByName(procName))
                {
                    if (p.Id != currentId)
                    {
                        try { p.Kill(); }
                        catch { }
                    }
                }

                instanceMutex = new Mutex(true, "DrugInfoWsServerMutex", out _);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        static void Main(string[] args)
        {
            EnsureSingleInstance();
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

            if (Environment.UserInteractive && !IsAdministrator())
            {
                Console.WriteLine("请以管理员身份运行本程序。");
                return;
            }

            if (!Environment.UserInteractive)
            {
                ServiceBase.Run(new DrugInfoWsService());
            }
            else
            {
                RunInteractive();
            }
        }

        public static void EnsureDatabase(string dbPath)
        {
            try
            {
                if (!File.Exists(dbPath))
                {
                    SQLiteConnection.CreateFile(dbPath);
                }

                using (SQLiteConnection conn = new SQLiteConnection(string.Format("Data Source={0};Version=3;", dbPath)))
                {
                    conn.Open();
                    string createTableSql = @"
                        CREATE TABLE IF NOT EXISTS druginfo (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            name TEXT NOT NULL,
                            manu_lotnum TEXT,
                            manu_date TEXT,
                            expy_end TEXT,
                            YMMC TEXT,
                            kcsb INTEGER DEFAULT 0,
                            msg TEXT,
                            create_time DATETIME DEFAULT CURRENT_TIMESTAMP
                        )";
                    using (SQLiteCommand cmd = new SQLiteCommand(createTableSql, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("数据库创建失败: " + ex.Message);
            }
        }

        static void RunInteractive()
        {
            Console.WriteLine("=== 药品信息WS WebSocket服务器 ===");
            Console.WriteLine("适配.NET 2.0 + SQLite3（无SSL加密）");
            Console.WriteLine();

            int serverPort = 26662; // WS端口（非SSL）
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string dbPath = Path.Combine(exeDir, "druginfo.db");
            EnsureDatabase(dbPath);

            try
            {
                DrugInfoWsWebSocketServer server = new DrugInfoWsWebSocketServer(serverPort, dbPath);

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
