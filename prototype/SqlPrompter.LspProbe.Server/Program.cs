using System.Text;
using System.Text.Json.Nodes;

// A minimal language server over stdio for the architecture review (#183): it answers initialize and
// textDocument/completion with one item and logs every message, so the language client of SSMS can be observed.
var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPrompter", "lspprobe.log");
Directory.CreateDirectory(Path.GetDirectoryName(log));
void Log(string text) { lock (log) File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine); }
Log("server started, pid " + Environment.ProcessId);

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();

string ReadMessage()
{
    var length = -1;
    while (true)
    {
        var line = new StringBuilder();
        int b;
        while ((b = input.ReadByte()) >= 0 && b != '\n') { if (b != '\r') line.Append((char)b); }
        if (b < 0) return null;
        if (line.Length == 0) break;
        if (line.ToString().StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.ToString().Substring(15).Trim());
    }

    var buffer = new byte[length];
    var read = 0;
    while (read < length) { var n = input.Read(buffer, read, length - read); if (n <= 0) return null; read += n; }
    return Encoding.UTF8.GetString(buffer);
}

void Send(JsonNode node)
{
    var body = Encoding.UTF8.GetBytes(node.ToJsonString());
    var header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length + "\r\n\r\n");
    output.Write(header); output.Write(body); output.Flush();
}

string message;
while ((message = ReadMessage()) != null)
{
    var json = JsonNode.Parse(message);
    var method = (string)json["method"];
    var id = json["id"];
    Log("<- " + method + (id != null ? " #" + id : "") + " " + (message.Length > 300 ? message.Substring(0, 300) + "..." : message));
    switch (method)
    {
        case "initialize":
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = new JsonObject { ["capabilities"] = new JsonObject {
                ["textDocumentSync"] = 1,
                ["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray(".", " ") } } } });
            break;
        case "textDocument/completion":
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = new JsonObject { ["isIncomplete"] = false, ["items"] = new JsonArray(
                new JsonObject { ["label"] = "lsp_hello", ["kind"] = 1, ["detail"] = "from the language server" }) } });
            break;
        case "shutdown":
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = null });
            break;
        case "exit":
            Log("exit");
            return;
        default:
            if (id != null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = null });
            break;
    }
}

Log("input closed");
