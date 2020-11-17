using General;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace TCPClient
{
    public static class ClientProgram
    {
        /// <summary>
        /// Событие для разделения доступа потоков к методу <see cref="ConsoleMessage(string)"/>  из разных потоков
        /// </summary>
        private static readonly AutoResetEvent ConsoleEvent = new AutoResetEvent(true);
        /// <summary>
        /// Регулярка для Ай-пи сервера
        /// </summary>
        private static readonly Regex regexIp = new Regex("^[0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3}$");
        /// <summary>
        /// Регулярка для имени пользователя
        /// </summary>
        private static readonly Regex regexClient = new Regex("^\\w{1,10}$");
        /// <summary>
        /// Данные клиента для общения
        /// </summary>
        private static Client Sender { get; set; }
        /// <summary>
        /// Ай-пи сервера
        /// </summary>
        private static string ServerIp { get; set; }
        /// <summary>
        /// Клиентский сокет
        /// </summary>
        public static Socket ClientSocket { get; set; }
        /// <summary>
        /// Очередь входящих сообщений (десериализованных из Json). Ждут когда их обработают
        /// </summary>
        private static ConcurrentQueue<Message> IncomingMessagesQueue { get; set; } = new ConcurrentQueue<Message>();
        /// <summary>
        /// Флаг того, является ли пользователь зарегистрированным
        /// </summary>
        private static bool IsRegistered { get; set; } = false;
        /// <summary>
        /// номер строки для вывода сообщений в консоль
        /// </summary>
        private static int DialogTop { get; set; } = 0;
        /// <summary>
        /// Номер строки для ввода сообщений пользователем в консоль
        /// </summary>
        private static int SenderTop { get; set; } = 0;
        /// <summary>
        /// Позиция курсора в строке ввода сообщения консоли
        /// </summary>
        private static int SenderLeft { get; set; } = 0;
        /// <summary>
        /// Получение сообщения от сервера
        /// </summary>
        /// <returns></returns>
        private static Message GetMessage(bool wait = false)
        {
            try
            {
                if (ClientSocket?.Connected != true || (!wait && ClientSocket?.Available == 0))
                {
                    Thread.Sleep(50);
                    return null;
                }

                var bytes = new byte[1024];
                var bytesLength = ClientSocket.Receive(bytes);
                if (bytesLength == 0)
                {
                    return null;
                }
                var jsonMessage = Encoding.UTF8.GetString(bytes, 0, bytesLength);
                if (!string.IsNullOrWhiteSpace(jsonMessage) && jsonMessage.StartsWith('{') && jsonMessage.EndsWith('}'))
                {
                    return JsonSerializer.Deserialize<Message>(jsonMessage);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Метод прриема входящих сообщений
        /// </summary>
        private static void ReceivingMessages()
        {
            while (ClientSocket?.Connected == true)
            {
                try
                {
                    var message = GetMessage();
                    if (message != null)
                    {
                        IncomingMessagesQueue.Enqueue(message);
                    }
                }
                catch (Exception ex)
                {
                    ConsoleMessage($"При получении сообщения возникло исключение: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Обработка входящих сообщений
        /// </summary>
        private static void ProcessingIncomingMessage()
        {
            while (ClientSocket?.Connected == true || IncomingMessagesQueue.Any())
            {
                try
                {
                    if (!IncomingMessagesQueue.TryDequeue(out var message))
                    {
                        continue;
                    }
                    var mess = "";
                    switch (message.Type)
                    {
                        case MessageType.Authorization:
                            Console.ForegroundColor = ConsoleColor.Green;
                            break;
                        case MessageType.Server:
                            Console.ForegroundColor = ConsoleColor.Red;
                            break;
                        case MessageType.Private:
                            Console.ForegroundColor = ConsoleColor.Blue;
                            mess += "!";
                            break;
                        case MessageType.All:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            break;
                    }
                    ConsoleMessage($"{mess}[{message.Sender.NickName}]: {message.Content}");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    ConsoleMessage($"При обработке сообщения возникло исключение: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Основной метод (точка входа)
        /// </summary>
        static void Main()
        {
            Console.SetCursorPosition(SenderLeft, SenderTop);
            if (!GetConnect())
            {
                ConsoleMessage("Всего доброго");
                Console.ReadLine();
                return;
            }
            if (!RegisterClient())
            {
                ClientSocket?.Disconnect(false);
                ClientSocket?.Shutdown(SocketShutdown.Both);
                ClientSocket?.Close();
                ConsoleMessage("Всего доброго");
                Console.ReadLine();
                return;
            }
            var receivingThread = new Thread(() => ReceivingMessages());
            receivingThread.Start();
            var processingThread = new Thread(() => ProcessingIncomingMessage());
            processingThread.Start();
            var command = "";
            while (command != Datas.ExitCommand.ComKey)
            {
                command = ConsoleRead();
                CreateMessage(command)?.SendAsync();
            }
            ClientSocket.Disconnect(false);
            ConsoleMessage("Всего доброго! Для выхода нажмите 'Enteer'");
            Console.ReadLine();
            receivingThread.Join();
            processingThread.Join();
            ClientSocket.Shutdown(SocketShutdown.Both);
            ClientSocket.Close();
        }

        /// <summary>
        /// Регистрация клиента в чате
        /// </summary>
        /// <returns></returns>
        private static bool RegisterClient()
        {
            while (!IsRegistered)
            {
                ConsoleMessage("Для регистрации в чате введите имя пользователя");
                var data = ConsoleRead();
                if (data == "Пока")
                {
                    return false;
                }
                if (string.IsNullOrWhiteSpace(data) || !regexClient.IsMatch(data))
                {
                    ConsoleMessage("Неверный формат данных");
                    continue;
                }
                Sender = new Client
                {
                    NickName = data
                };
                var message = new Message
                {
                    Type = MessageType.Authorization,
                    Sender = Sender
                };
                message?.SendAsync();
                var responce = GetMessage(true);
                IsRegistered = responce != null && responce.Content.StartsWith("Ok");
                if (IsRegistered)
                {
                    responce.Content = responce.Content.Substring(3);
                    IncomingMessagesQueue.Enqueue(responce);
                }
            }
            return true;
        }

        /// <summary>
        /// Присоединение клиента к серверу
        /// </summary>
        /// <returns></returns>
        private static bool GetConnect()
        {
            while (ClientSocket?.Connected != true)
            {
                ConsoleMessage("Для входа в чат введите IP адрес сервера. Пример: 192.168.0.155");
                var data = ConsoleRead();
                if (data == "Пока")
                {
                    return false;
                }
                if (string.IsNullOrWhiteSpace(data) || !regexIp.IsMatch(data))
                {
                    ConsoleMessage("Неверный формат данных");
                    continue;
                }
                ServerIp = data;
                try
                {
                    var ipHost = Dns.GetHostEntry(ServerIp);
                    var ipAddr = ipHost.AddressList[0];
                    var ipEndPoint = new IPEndPoint(ipAddr, Datas.ServerListeningPort);
                    ClientSocket = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    ClientSocket.Connect(ipEndPoint);
                    if (!ClientSocket.Connected)
                    {
                        ConsoleMessage($"Не удалось подключиться к серверу {data}");
                    }
                }
                catch
                {
                    ConsoleMessage("Не удалось подключиться к серверу.");
                }
            }
            ConsoleMessage("Вы успешно подсоединились к серверу.");
            return true;
        }

        /// <summary>
        /// Создание сообщения из того что ввел пользователь
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static Message CreateMessage(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }
            var message = new Message();

            if (data.StartsWith("Сервер."))
            {
                message.Type = MessageType.Server;
                message.Content = data;
                if (data == "Сервер.")
                {
                    ConsoleMessage($"Отсутствует вопрос серверу.");
                    return null;
                }
            }
            else if (data.StartsWith("!"))
            {
                message.Type = MessageType.Private;
                data = data.Replace("!", "");
                if (!data.Contains(':'))
                {
                    ConsoleMessage($"Неверный формат личного сообщений. Отсутствует \":\", отделяющее сообщение от адресата.");
                    return null;
                };
                var datas = data.Split(':');
                if (string.IsNullOrWhiteSpace(datas[0]))
                {
                    ConsoleMessage($"Неверный формат личного сообщений. Не указан адресат.");
                    return null;
                }
                message.RecipientNickName = datas[0];
                message.Content = string.Join(':', datas.Where(x => x != datas[0]));
            }
            else
            {
                message.Type = MessageType.All;
                message.RecipientNickName = "";
                message.Content = data;
            }

            message.Sender = Sender;
            return message;
        }

        /// <summary>
        /// Вывод сообщений на консоль
        /// </summary>
        /// <param name="mes"></param>
        private static void ConsoleMessage(string mes)
        {
            mes = $"{DateTime.Now}: {mes}";
            var strings = mes.Split('\n');
            var rowCount = 0;
            foreach (var str in strings)
            {
                rowCount += str.Length / Console.WindowWidth + 1;
            }
            Console.MoveBufferArea(0, SenderTop, Console.WindowWidth, Console.CursorTop, 0, SenderTop + rowCount);
            SenderLeft = Console.CursorLeft;
            SenderTop =  Console.CursorTop + rowCount;

            Console.SetCursorPosition(0, DialogTop);
            Console.Write(mes);
            DialogTop += rowCount;
            Console.SetCursorPosition(SenderLeft, SenderTop);
        }

        /// <summary>
        /// Считывание сообщения с консоли
        /// </summary>
        /// <returns></returns>
        private static string ConsoleRead()
        {
            Console.SetCursorPosition(SenderLeft, SenderTop);
            Console.Write("[Вы]: ");
            var text = Console.ReadLine();
            var deltaTop = Console.CursorTop - SenderTop;
            Console.MoveBufferArea(0, SenderTop, 0, Console.CursorTop, 0, DialogTop + 1);
            SenderLeft = 0;
            SenderTop += deltaTop;
            DialogTop += deltaTop;
            return text;
        }

        /// <summary>
        /// Отправка сообщения
        /// </summary>
        private static void SendAsync(this Message message)
        {
            var jsonMessage = JsonSerializer.Serialize(message);
            jsonMessage = Regex.Replace(jsonMessage, @"\\u([0-9A-Fa-f]{4})", m => "" + (char)Convert.ToInt32(m.Groups[1].Value, 16));
            var jsonStringMessage = Encoding.UTF8.GetBytes(jsonMessage);
            if (ClientSocket?.Connected == true)
            {
                ClientSocket?.SendAsync(jsonStringMessage, SocketFlags.None);
                return;
            }
            ConsoleMessage("Соединение с сервером прервано. Сообщение не отправлено.");
        }
    }
}

