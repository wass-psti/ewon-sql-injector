using Newtonsoft.Json.Linq;

Console.WriteLine("Paste JSON data:");

string? json = Console.ReadLine();

try
{
    JObject data = JObject.Parse(json!);

    Console.WriteLine();
    Console.WriteLine("=== PARSED DATA ===");

    Console.WriteLine($"Device: {data["device"]}");
    Console.WriteLine($"Timestamp: {data["timestamp"]}");

    Console.WriteLine();

    JArray? tags = data["tags"] as JArray;

    if (tags != null)
    {
        foreach (JObject tag in tags)
        {
            Console.WriteLine($"Tag Name : {tag["name"]}");
            Console.WriteLine($"Value    : {tag["value"]}");
            Console.WriteLine($"Unit     : {tag["unit"]}");
            Console.WriteLine("--------------------");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("Parsing failed.");
    Console.WriteLine($"Error: {ex.Message}");
}