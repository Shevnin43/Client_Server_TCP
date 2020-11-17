using System;
using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace General
{
    public class Client
    {
        /// <summary>
        /// Сокет клиента (не сериализуется в json
        /// </summary>
        [JsonIgnore]
        public Socket Handler { get; set; }
        /// <summary>
        /// Id клиента. Не особо нужен, больше для идентификации
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();
        /// <summary>
        /// Ник клиента
        /// </summary>
        public string NickName { get; set; } = "";

    }
}
