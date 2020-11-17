using System;
using System.Linq;

namespace General
{
    /// <summary>
    /// Тип сообщения
    /// </summary>
    public enum MessageType
    {
        Authorization,
        All,
        Server,
        Private
    }

    /// <summary>
    /// Класс сообщений между клиентами и сервером
    /// </summary>
    public class Message
    {
        /// <summary>
        /// Тип сообщения
        /// </summary>
        public MessageType Type { get; set; }
        /// <summary>
        /// Отправитель сообщения
        /// </summary>
        public Client Sender { get; set; }
        /// <summary>
        /// Ник (имя) получателя сообщения
        /// </summary>
        public string RecipientNickName { get; set; }
        /// <summary>
        /// Содержимое сообщения
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Клонирует объект с сохранением всех свойств
        /// </summary>
        /// <returns></returns>
        public Message Clone() => Clone(null);

        /// <summary>
        /// Клонирует объект с сохранением значений в указанных свойствах СВОЙСТВА ПИШЕМ В ОДНУ СТРОКУ ЧЕРЕЗ ЗАПЯТУЮ
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public Message Clone(string fields = null)
        {
            var result = new Message();
            var properties = GetType().GetProperties().Where(x => x.SetMethod != null);
            var workFields = fields?.Replace(" ", "")?.Split(',') ?? properties.Select(x => x.Name);
            foreach (var field in workFields)
            {
                if (properties.Select(x => x.Name).Contains(field))
                {
                    var xxx = properties.FirstOrDefault(x => x.Name.Equals(field)).GetValue(this, null);
                    result.GetType().GetProperty(field).SetValue(result, xxx);
                }
            }
            return result;
        }

        /// <summary>
        /// Клонирует объект за исключением указанных свойств
        /// </summary>
        /// <param name="exceptFields"></param>
        /// <returns></returns>
        public Message CloneExcept(string exceptFields) => Clone(string.Join(", ", GetType().GetProperties().Where(x => x.SetMethod != null && !exceptFields.Replace(" ", "").Split(',').Contains(x.Name)).Select(x => x.Name)));

    }
}
