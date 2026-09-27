using System.Net.Sockets;
using System.Text;

const string serverAddress = "127.0.0.1";
const int serverPort = 5000;

TcpClient client = new TcpClient();

try
{
    await client.ConnectAsync(serverAddress, serverPort);

    Console.WriteLine("Підключено до сервера.");
    Console.WriteLine();

    using NetworkStream stream = client.GetStream();

    string loginRequest = await ReceiveMessage(stream);

    if (loginRequest == "LOGIN")
    {
        Console.Write("Введіть логін: ");
        string login = Console.ReadLine() ?? "";

        await SendMessage(stream, login);

        string passwordRequest = await ReceiveMessage(stream);

        if (passwordRequest == "ERROR_LOGIN")
        {
            Console.WriteLine("Помилка: невірний логін.");
            return;
        }

        if (passwordRequest == "PASSWORD")
        {
            Console.Write("Введіть пароль: ");
            string password = Console.ReadLine() ?? "";

            await SendMessage(stream, password);

            string result = await ReceiveMessage(stream);

            if (result == "ERROR_PASSWORD")
            {
                Console.WriteLine("Помилка: невірний пароль.");
                return;
            }

            if (result == "SUCCESS")
            {
                Console.WriteLine();
                Console.WriteLine("Авторизація успішна!");
                Console.WriteLine();

                await ShowMenu(stream);
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Помилка: {ex.Message}");
}
finally
{
    client.Close();
}

async Task ShowMenu(NetworkStream stream)
{
    while (true)
    {
        Console.WriteLine("========== МЕНЮ ==========");
        Console.WriteLine("1. Отримати список файлів на сервері");
        Console.WriteLine("2. Отримати файл з сервера");
        Console.WriteLine("3. Передати файл на сервер");
        Console.WriteLine("4. Вийти");
        Console.WriteLine("===========================");
        Console.Write("Ваш вибір: ");

        string choice = Console.ReadLine() ?? "";

        Console.WriteLine();

        if (choice == "1")
        {
            await GetFileList(stream);
        }
        else if (choice == "2")
        {
            await DownloadFile(stream);
        }
        else if (choice == "3")
        {
            await UploadFile(stream);
        }
        else if (choice == "4")
        {
            await SendMessage(stream, "EXIT");
            Console.WriteLine("Ви вийшли з програми.");
            break;
        }
        else
        {
            Console.WriteLine("Невірний вибір.");
        }

        Console.WriteLine();
    }
}

async Task GetFileList(NetworkStream stream)
{
    await SendMessage(stream, "LIST");

    string result = await ReceiveMessage(stream);

    Console.WriteLine("Файли на сервері:");

    if (result == "Папка порожня.")
    {
        Console.WriteLine("Папка порожня.");
    }
    else
    {
        Console.WriteLine(result);
    }
}

async Task DownloadFile(NetworkStream stream)
{
    await SendMessage(stream, "LIST");

    string result = await ReceiveMessage(stream);

    if (result == "Папка порожня.")
    {
        Console.WriteLine("На сервері немає файлів.");
        return;
    }

    string[] files = result.Split('\n');

    Console.WriteLine("Файли на сервері:");

    for (int i = 0; i < files.Length; i++)
    {
        Console.WriteLine($"{i + 1}. {files[i]}");
    }

    Console.Write("Оберіть номер файлу: ");

    if (!int.TryParse(Console.ReadLine(), out int number))
    {
        Console.WriteLine("Невірний номер.");
        return;
    }

    if (number < 1 || number > files.Length)
    {
        Console.WriteLine("Такого файлу немає.");
        return;
    }

    string fileName = files[number - 1];

    await SendMessage(stream, "DOWNLOAD");
    await SendMessage(stream, fileName);

    string fileStatus = await ReceiveMessage(stream);

    if (fileStatus == "FILE_NOT_FOUND")
    {
        Console.WriteLine("Файл не знайдено.");
        return;
    }

    string fileSizeText = await ReceiveMessage(stream);

    long fileSize = long.Parse(fileSizeText);

    string downloadsFolder = "Downloads";

    Directory.CreateDirectory(downloadsFolder);

    string filePath = Path.Combine(downloadsFolder, Path.GetFileName(fileName));

    byte[] buffer = new byte[8192];

    long received = 0;

    using FileStream fileStream = File.Create(filePath);

    while (received < fileSize)
    {
        int bytesToRead = (int)Math.Min(buffer.Length, fileSize - received);

        int bytesRead = await stream.ReadAsync(
            buffer.AsMemory(0, bytesToRead));

        if (bytesRead == 0)
            break;

        await fileStream.WriteAsync(
            buffer.AsMemory(0, bytesRead));

        received += bytesRead;
    }

    Console.WriteLine($"Файл отримано: {fileName}");
    Console.WriteLine($"Збережено в: {Path.GetFullPath(filePath)}");
}

async Task UploadFile(NetworkStream stream)
{
    Console.Write("Введіть шлях до файлу: ");

    string filePath = Console.ReadLine() ?? "";

    filePath = filePath.Trim('"');

    if (!File.Exists(filePath))
    {
        Console.WriteLine("Файл не знайдено.");
        return;
    }

    FileInfo fileInfo = new FileInfo(filePath);

    string fileName = fileInfo.Name;

    await SendMessage(stream, "UPLOAD");
    await SendMessage(stream, fileName);
    await SendMessage(stream, fileInfo.Length.ToString());

    byte[] buffer = new byte[8192];

    using FileStream fileStream = File.OpenRead(filePath);

    int bytesRead;

    while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
    {
        await stream.WriteAsync(
            buffer.AsMemory(0, bytesRead));
    }

    string result = await ReceiveMessage(stream);

    if (result == "UPLOAD_SUCCESS")
    {
        Console.WriteLine("Файл успішно передано на сервер.");
    }
}

async Task SendMessage(NetworkStream stream, string message)
{
    byte[] data = Encoding.UTF8.GetBytes(message);

    byte[] length = BitConverter.GetBytes(data.Length);

    await stream.WriteAsync(length);
    await stream.WriteAsync(data);
}

async Task<string> ReceiveMessage(NetworkStream stream)
{
    byte[] lengthBytes = new byte[4];

    await ReadExactly(stream, lengthBytes);

    int length = BitConverter.ToInt32(lengthBytes);

    byte[] data = new byte[length];

    await ReadExactly(stream, data);

    return Encoding.UTF8.GetString(data);
}

async Task ReadExactly(NetworkStream stream, byte[] buffer)
{
    int offset = 0;

    while (offset < buffer.Length)
    {
        int bytesRead = await stream.ReadAsync(
            buffer.AsMemory(offset, buffer.Length - offset));

        if (bytesRead == 0)
            throw new Exception("Сервер закрив з'єднання.");

        offset += bytesRead;
    }
}