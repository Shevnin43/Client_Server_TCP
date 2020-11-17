using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using General;

namespace TCPServer
{
    public static class ClientProgram
    {
        /// <summary>
        /// Событие для разделения доступа потоков к методу <see cref="GoodByClient(Client, bool)"/>  из разных потоков
        /// </summary>
        private static readonly AutoResetEvent GoodByEvent = new AutoResetEvent(true);
        /// <summary>
        /// Событие для разделения доступа потоков к методу <see cref="ServerMessage(string)"/> из разных потоков
        /// </summary>
        private static readonly AutoResetEvent ConsoleEvent = new AutoResetEvent(true);
        /// <summary>
        /// Прослушиваемый сокет
        /// </summary>
        private static Socket Listener;
        /// <summary>
        /// Данные сервера
        /// </summary>
        private static readonly string ServerIp = Dns.GetHostAddresses(Dns.GetHostName())[1].ToString();
        private static readonly string ServerName = Dns.GetHostName();
        /// <summary>
        /// Флаг работы сервера. По выключению данного флага происходит завершение работы всех трех потоков
        /// </summary>
        private static bool IsMakedUp { get; set; } = false;
        /// <summary>
        /// Данные текущего сервера
        /// </summary>
        public static Client ServerData = new Client
        {
            NickName = $"Сервер-{ServerName}",
        };
        /// <summary>
        /// Список всех пользователей которые сейчас в чате
        /// </summary>
        private static readonly List<Client> Users = new List<Client>();
        /// <summary>
        /// Очередь входящих сообщений (десериализованных из Json). Ждут когда их обработают
        /// </summary>
        private static ConcurrentQueue<Message> IncomingMessagesQueue { get; set; } = new ConcurrentQueue<Message>();
        /// <summary>
        /// Очередь исходящих сообщений. Ждут отправления.
        /// </summary>
        private static ConcurrentQueue<Message> OutgoingMessageQueue { get; set; } = new ConcurrentQueue<Message>();

        /// <summary>
        /// Вывод сообщений на сервере
        /// </summary>
        /// <param name="mes"></param>
        private static void ServerMessage(string mes)
        {
            ConsoleEvent.WaitOne();
            Console.WriteLine($"{DateTime.Now}: {mes}");
            ConsoleEvent.Set();
        }
        /// <summary>
        /// Варианты ответов на вопрос "чем занимаешься?"
        /// </summary>
        private static readonly string[] Businesses = new string[]
        {
            "Балду гоняю...",
            "Сижу, смотрю как вы тупите...",
            "Курю бамбук...",
            "Поуши в работе...",
            "Я на работе работу работаю, но не перерабатывю...",
            "За Вами подглядываю...",
            "В основном - косячу с перерывами на чай и обед..."
        };
        /// <summary>
        /// Варианты ответов на вопрос "Как дела?"
        /// </summary>
        private static readonly string[] Affairs = new string[]
        {
            "Пока не родила...",
            "Всё нормуль ...",
            "Бывало и лучше, могло быть и хуже...",
            "Дела у прокурора, у нас так, - делишки...",
            "Как сажа бела ...",
            "Всё прекрасно...",
            "Потихоньку...",
            "Не хуже чем у Вас...",
            "Спасибо, Вашими молитвами..."
        };
        /// <summary>
        /// Перечень возможных команд
        /// </summary>
        private static readonly List<Command> AllCommands = new List<Command>
        {
            new Command("Сервер.Команды?", "Вывод всех команд", (s) => string.Concat($"{AllCommandsString}", $"\n{Datas.ExitCommand.ComKey} --> {Datas.ExitCommand.Comment}")),
            new Command("Сервер.Кто в чате?", "Вывод всех пользователей", (s) => string.Join('\n', Users.Select(x => x.NickName))),
            new Command("Сервер.Как дела?", "Вопрос серверу", (s) => Affairs[new Random().Next(0, Affairs.Length-1)]),
            new Command("Сервер.День?", "Вопрос серверу", (s) => DateTime.Today.DayOfWeek.ToString()),
            new Command("Сервер.Число?", "Вопрос серверу", (s) => DateTime.Now.ToShortDateString()),
            new Command("Сервер.Время?", "Вопрос серверу", (s) => DateTime.Now.ToShortTimeString()),
            new Command("Сервер.Чем занимаешься?", "Вопрос серверу", (s) => Businesses[new Random().Next(0, Businesses.Length-1)]),
            new Command("!<имя>:...", "Личное сообщение '...' пользователю <имя>", null)
        };
        /// <summary>
        /// Строка - собиратель для всех команд
        /// </summary>
        private static readonly string AllCommandsString = "\n" + string.Join('\n', AllCommands.Select(x => $"{x.ComKey} --> {x.Comment}"));
        /// <summary>
        /// Удаление клиента из чата
        /// </summary>
        /// <param name="sender"></param>
        /// <returns></returns>
        private static void GoodByClient(Client sender, bool fromChecking = false)
        {
            GoodByEvent.WaitOne();
            try
            {
                var user = Users.FirstOrDefault(x => x.Id == sender.Id && x.NickName == sender.NickName);
                if (user == null)
                {
                    ServerMessage($"Пользователь {sender.NickName} взял самоотвод но его не оказалось в списке пользователей.");
                    return;
                }
                bool isRemoved;
                lock (Users)
                {
                    isRemoved = Users.Remove(user);
                }
                if (isRemoved)
                {
                    lock (user)
                    {
                        if (!fromChecking)
                        {
                            user.Handler.Disconnect(false);
                        }
                        user.Handler.Shutdown(SocketShutdown.Both);
                        user.Handler.Close();
                    }
                }
                ServerMessage($"Пользователь [{sender.NickName}] " + (fromChecking ? "отвалился." : "успешно взял самоотвод."));
            }
            finally
            {
                GoodByEvent.Set();
            }

        }

        /// <summary>
        /// Ожидание сообщения по сокету конкретного клиента в отедльном потоке
        /// </summary>
        /// <param name="handler"></param>
        private static void ReciveClient(Socket handler)
        {
            while (handler?.Connected == true && IsMakedUp)
            {
                try
                {
                    var bytes = new byte[1024];
                   if (handler?.Available == 0)
                    {
                        Thread.Sleep(50);
                        continue;
                    }
                    var bytesLength = handler.Receive(bytes);
                    if (bytesLength == 0)
                    {
                        continue;
                    }
                    var fullData = Encoding.UTF8.GetString(bytes, 0, bytesLength);
                    var message = JsonSerializer.Deserialize<Message>(fullData);
                    message.Sender.Handler = handler;
                    if (message != null)
                    {
                        IncomingMessagesQueue.Enqueue(message);
                    }
                }
                catch (Exception ex)
                {
                    ServerMessage($"При получении сообщения возникло исключение: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Получение входящих сообщений
        /// </summary>
        private static void ConnectingClients()
        {
            var threads = new List<Thread>();
            while (IsMakedUp)
            {
                try
                {
                    var handler = Listener.Accept();
                    var clientThread = new Thread(() => ReciveClient(handler));
                    clientThread.Start();
                    threads.Add(clientThread);
                    ServerMessage($"К серверу прицепился новый клиент: {handler.RemoteEndPoint}");
                }
                catch (SocketException){}
                catch (Exception ex)
                {
                    ServerMessage($"При подключении клиента возникло исключение: {ex.Message}");
                }
            }
            foreach (var thread in threads)
            {
                thread.Join();
            }
        }

        /// <summary>
        /// Обработка входящих сообщений
        /// </summary>
        private static void ProcessingIncomingMessage()
        {
            while (IsMakedUp || IncomingMessagesQueue.Any())
            {
                try
                {
                   if (!IncomingMessagesQueue.TryDequeue(out var message))
                    {
                        continue;
                    }
                    if (message.Type != MessageType.Authorization && !Users.Any(x => x.Id == message.Sender.Id && x.NickName == message.Sender.NickName && x.Handler.Connected))
                    {
                        OutgoingMessageQueue.Enqueue(message.ServerResponse($"Простите, {message.Sender.NickName}, но вас нет!"));
                        continue;
                    }
                    switch (message.Type)
                    {
                        case MessageType.Authorization:
                            if (Users.Any(x => x.NickName.Equals(message.Sender.NickName) || x.Id.Equals(message.Sender.Id)))
                            {
                                OutgoingMessageQueue.Enqueue(message.ServerResponse($"No/Пользователь с аналогичными регистрационными данными уже в чате", MessageType.Authorization));
                                break;
                            }
                            lock (Users)
                            {
                                Users.Add(message.Sender);
                            }
                            ServerMessage($"В чат вошел [{message.Sender.NickName}] ({message.Sender.Handler.RemoteEndPoint})");
                            var responceContent = $"Ok/Поздравляю {message.Sender.NickName}, Вы в чате. Список доступных команд:";
                            responceContent += AllCommandsString;
                            responceContent += $"\n{Datas.ExitCommand.ComKey} --> {Datas.ExitCommand.Comment}";
                            OutgoingMessageQueue.Enqueue(message.ServerResponse(responceContent, MessageType.Authorization));
                            break;
                        case MessageType.Server:
                            if(message.Content.Equals(Datas.ExitCommand.ComKey))
                            {
                                GoodByClient(message.Sender);
                                break;
                            }
                            var com = AllCommands.FirstOrDefault(x => x.ComKey.Equals(message.Content));
                            OutgoingMessageQueue.Enqueue(message.ServerResponse(com != null
                                ? com.Function?.Invoke("")
                                : "Мой юнный мозг не выдержал перегрузки, вызванной Вашим вопросом, и сдулся.")
                            );
                            break;
                        case MessageType.Private:
                            if (Users.Any(x => x.NickName == message.RecipientNickName))
                            {
                                OutgoingMessageQueue.Enqueue(message);
                                break;
                            }
                            OutgoingMessageQueue.Enqueue(message.ServerResponse( $"Получатель сообщения [{message.RecipientNickName}] отсутствует в списке зарегистрированных пользователей"));
                            break;
                        case MessageType.All:
                            foreach (var recipient in Users)
                            {
                                var answer = message.CloneExcept("RecipientNickName");
                                answer.RecipientNickName = recipient.NickName;
                                OutgoingMessageQueue.Enqueue(answer);
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    ServerMessage($"При обработке сообщения возникло исключение: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Обработка исходящих сообщений (Отправка)
        /// </summary>
        private static void ShipmentMessages()
        {
            while(IsMakedUp || OutgoingMessageQueue.Any())
            {
                if (!OutgoingMessageQueue.TryDequeue(out var message))
                {
                    continue;
                }
                try
                {
                    var json = JsonSerializer.Serialize(message);
                    json = Regex.Replace(json, @"\\u([0-9A-Fa-f]{4})", m => "" + (char)Convert.ToInt32(m.Groups[1].Value, 16));
                    var bytes = Encoding.UTF8.GetBytes(json);
                    var recipient = Users.FirstOrDefault(x => x.NickName == message.RecipientNickName);
                    if (recipient?.Handler?.Connected == true)
                    {
                        recipient.Handler.SendAsync(bytes, SocketFlags.None);
                        ServerMessage(message.Sender.Id == ServerData.Id ? $"Ответ Сервера клиенту [{recipient.NickName}] отправлен." : $"Сообщение от [{message.Sender.NickName}] получателю [{recipient.NickName}] отправлено.");
                        continue;
                    }
                    ServerMessage($"Попытка отправить сообщение пользователю {message.RecipientNickName} который уже покинул нас");
                }
                catch (Exception ex)
                {
                    ServerMessage($"При отправке сообщения возникло исключение: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Забавная штука - проверяет пользователей в коллекциях Users и RemovingUsers на предмет дисконнекта и выключает их сокет и удаляет из соответствующего списка (в случае дисконнекта соответственно)
        /// </summary>
        private static void CheckUsersConnect()
        {
            const int delay = 100;
            const int timeout = 1000 * 60 * 2;
            var timer = 0;
            while (IsMakedUp)
            { 
                if (timer >= timeout)
                {
                    Client client;
                    do
                    {
                        client = Users.FirstOrDefault(x => x.Handler?.Connected == false);
                        if (client == null)
                        {
                            break;
                        }
                        GoodByClient(client, true);
                    }
                    while (client != null);
                    timer = 0;
                }
                else
                {
                    timer += delay;
                }
                Thread.Sleep(delay);
            }
        }

        /// <summary>
        /// Точка входа
        /// </summary>
        static void Main()
        {
            var ipHost = Dns.GetHostEntry(ServerIp);
            var ipAddr = ipHost.AddressList[0];
            var ipEndPoint = new IPEndPoint(ipAddr, Datas.ServerListeningPort);
            Listener = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            Listener.Bind(ipEndPoint);
            Listener.Listen(1000);
            ServerMessage($"Сервер [{ServerName}] запущен.") ;
            IsMakedUp = true;

            const string endCommand = "Дасвидания";
            var incomingThread = new Thread(() => ConnectingClients());
            incomingThread.Start();
            var processingThread = new Thread(() => ProcessingIncomingMessage());
            processingThread.Start();
            var outgoingThread = new Thread(() => ShipmentMessages());
            outgoingThread.Start();
            var usersCheckThread = new Thread(() => CheckUsersConnect());
            usersCheckThread.Start();

            Console.WriteLine($"Введите '{endCommand}' для выхода.");
            var userCommand = "";
            while (!userCommand.Equals(endCommand))
            {
                userCommand = Console.ReadLine();
            }
            IsMakedUp = false;
            Listener.Close();
            incomingThread.Join();
            processingThread.Join();
            outgoingThread.Join();
            usersCheckThread.Join();
        }

        /// <summary>
        /// Подготавливает ответ от сервера клиенту с указанным контентом
        /// </summary>
        /// <param name="content"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        private static Message ServerResponse( this Message message, string content, MessageType type = MessageType.Server)
        {
            return new Message
            {
                Content = content,
                Sender = ServerData,
                RecipientNickName = message.Sender.NickName,
                Type = type
            };
        }
    }
}
