using System.Net;
using System.Net.Sockets;
using System.Text;

const int port = 5000;
const string serverFilesFolder = "ServerFiles";

Directory.CreateDirectory(serverFilesFolder);

TcpListener server = new TcpListener(IPAddress.Any, port);
server.Start();

Console.WriteLine("Сервер запущено.");
Console.WriteLine($"Порт: {port}");
Console.WriteLine($"Папка файлів: {Path.GetFullPath(serverFilesFolder)}");
Console.WriteLine();

while (true)
{
    TcpClient client = await server.AcceptTcpClientAsync();

    Console.WriteLine("Підключився новий клієнт.");

    _ = Task.Run(() => HandleClient(client));
}

async Task HandleClient(TcpClient client)
{
    using NetworkStream stream = client.GetStream();

    try
    {
        await SendMessage(stream, "LOGIN");

        string login = await ReceiveMessage(stream);

        if (login != "admin")
        {
            await SendMessage(stream, "ERROR_LOGIN");
            client.Close();
            return;
        }

        await SendMessage(stream, "PASSWORD");

        string password = await ReceiveMessage(stream);

        if (password != "password")
        {
            await SendMessage(stream, "ERROR_PASSWORD");
            client.Close();
            return;
        }

        await SendMessage(stream, "SUCCESS");

        Console.WriteLine("Клієнт успішно авторизувався.");

        while (true)
        {
            string command = await ReceiveMessage(stream);

            if (command == "LIST")
            {
                await SendFileList(stream);
            }
            else if (command == "DOWNLOAD")
            {
                string fileName = await ReceiveMessage(stream);
                await SendFileToClient(stream, fileName);
            }
            else if (command == "UPLOAD")
            {
                await ReceiveFileFromClient(stream);
            }
            else if (command == "EXIT")
            {
                Console.WriteLine("Клієнт відключився.");
                break;
            }
        }
    }
    catch
    {
        Console.WriteLine("Клієнт відключився.");
    }
    finally
    {
        client.Close();
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

async Task SendFileList(NetworkStream stream)
{
    string[] files = Directory.GetFiles(serverFilesFolder);

    List<string> fileNames = new List<string>();

    foreach (string file in files)
    {
        fileNames.Add(Path.GetFileName(file));
    }

    string result;

    if (fileNames.Count == 0)
    {
        result = "Папка порожня.";
    }
    else
    {
        result = string.Join("\n", fileNames);
    }

    await SendMessage(stream, result);
}

async Task SendFileToClient(NetworkStream stream, string fileName)
{
    string safeFileName = Path.GetFileName(fileName);
    string filePath = Path.Combine(serverFilesFolder, safeFileName);

    if (!File.Exists(filePath))
    {
        await SendMessage(stream, "FILE_NOT_FOUND");
        return;
    }

    await SendMessage(stream, "FILE_FOUND");

    FileInfo fileInfo = new FileInfo(filePath);

    await SendMessage(stream, fileInfo.Length.ToString());

    byte[] buffer = new byte[8192];

    using FileStream fileStream = File.OpenRead(filePath);

    int bytesRead;

    while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
    {
        await stream.WriteAsync(buffer.AsMemory(0, bytesRead));
    }

    Console.WriteLine($"Файл відправлено: {safeFileName}");
}

async Task ReceiveFileFromClient(NetworkStream stream)
{
    string fileName = await ReceiveMessage(stream);

    string fileSizeText = await ReceiveMessage(stream);

    long fileSize = long.Parse(fileSizeText);

    string safeFileName = Path.GetFileName(fileName);

    string filePath = Path.Combine(serverFilesFolder, safeFileName);

    byte[] buffer = new byte[8192];

    long received = 0;

    using FileStream fileStream = File.Create(filePath);

    while (received < fileSize)
    {
        int bytesToRead = (int)Math.Min(buffer.Length, fileSize - received);

        int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead));

        if (bytesRead == 0)
            break;

        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));

        received += bytesRead;
    }

    await SendMessage(stream, "UPLOAD_SUCCESS");

    Console.WriteLine($"Отримано файл: {safeFileName}");
}

async Task ReadExactly(NetworkStream stream, byte[] buffer)
{
    int offset = 0;

    while (offset < buffer.Length)
    {
        int bytesRead = await stream.ReadAsync(
            buffer.AsMemory(offset, buffer.Length - offset));

        if (bytesRead == 0)
            throw new Exception("З'єднання закрито.");

        offset += bytesRead;
    }
}