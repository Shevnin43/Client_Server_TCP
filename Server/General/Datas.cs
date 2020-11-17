
namespace General
{
    public static class Datas
    {
        /// <summary>
        /// Порт сервера
        /// </summary>
        public const int ServerListeningPort = 11000;
        /// <summary>
        /// Команда на выход
        /// </summary>
        public static readonly Command ExitCommand = new Command("Сервер.Пока", "Команда для выхода из чата", null);
        
    }
}
