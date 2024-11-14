using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Hosting.Server;
using Newtonsoft.Json;

class ABXExchangeClient // Code By Moiz Khot (3012)
{
    private static string serverAddress;
    private static int serverPort;

    static void Main(string[] args)
    {
        try
        {
            // Load configuration from the JSON file
            LoadConfiguration("config.json");

            var client = new ABXExchangeClient();
            client.Run();
            Console.ReadLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    private static void LoadConfiguration(string configFilePath)
    {
        Console.WriteLine("into config");
        string binDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        // Navigate up to the project root directory
        string projectDirectory = Directory.GetParent(binDirectory).Parent.Parent.FullName;

        // Combine the project root directory path with the config file name
        string configFile = Path.Combine(projectDirectory, configFilePath);
        Console.WriteLine($"{configFile}");
        // Read the configuration file
        if (!File.Exists(configFile))
        {
            throw new FileNotFoundException($"Configuration file '{configFilePath}' not found.");
        }

        var jsonConfig = File.ReadAllText(configFile);
        var config = JsonConvert.DeserializeObject<Config>(jsonConfig);

        serverAddress = config.ServerAddress;
        serverPort = config.ServerPort;

        Console.WriteLine($"Configuration loaded: IP = {serverAddress}, Port = {serverPort}");
    }

    public void Run()
    {
        var packets = new List<StockPacket>();
        var missedSequences = new HashSet<int>();

        using (var tcpClient = new TcpClient(serverAddress, serverPort))
        using (var networkStream = tcpClient.GetStream())
        {
            // Step 1: Request all packets using Call Type 1 (Stream All Packets)
            SendRequest(networkStream, 1, 0);

            // Step 2: Receive packets and parse
            ReceivePackets(networkStream, packets, missedSequences);

            // Step 3: Handle any missing sequences
            foreach (var seq in missedSequences)
            {
                Console.WriteLine($"Requesting missing packet sequence: {seq}");
                SendRequest(networkStream, 2, (byte)seq); // Resend Packet (Call Type 2)
                ReceivePackets(networkStream, packets, new HashSet<int>());
            }

            // Step 4: Save the packets as a JSON file
            SavePacketsToJson(packets);
        }
    }

    private void SendRequest(NetworkStream networkStream, byte callType, byte resendSeq)
    {
        byte[] requestPayload = new byte[2];
        requestPayload[0] = callType;
        requestPayload[1] = resendSeq;
        networkStream.Write(requestPayload, 0, requestPayload.Length);
    }

    private void ReceivePackets(NetworkStream networkStream, List<StockPacket> packets, HashSet<int> missedSequences)
    {
        byte[] buffer = new byte[16]; // Assuming each packet is 16 bytes
        int bytesRead;

        while ((bytesRead = networkStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (bytesRead < buffer.Length) break; // incomplete packet, break the loop

            StockPacket packet = ParsePacket(buffer);
            packets.Add(packet);

            // Track missed sequences (Assumption: the packets arrive in sequence)
            if (missedSequences.Count == 0 || packets[packets.Count - 1].SequenceNumber != packets.Count)
            {
                missedSequences.Add(packets[packets.Count - 1].SequenceNumber);
            }
        }
    }

    private StockPacket ParsePacket(byte[] buffer)
    {
        // Extract fields based on the ABX Mock Exchange Packet Format
        string symbol = Encoding.ASCII.GetString(buffer, 0, 4).Trim();
        char buySellIndicator = (char)buffer[4];
        int quantity = BitConverter.ToInt32(new byte[] { buffer[8], buffer[9], buffer[10], buffer[11] }, 0);
        int price = BitConverter.ToInt32(new byte[] { buffer[12], buffer[13], buffer[14], buffer[15] }, 0);
        int sequence = BitConverter.ToInt32(new byte[] { buffer[4], buffer[5], buffer[6], buffer[7] }, 0);

        return new StockPacket
        {
            Symbol = symbol,
            BuySellIndicator = buySellIndicator,
            Quantity = quantity,
            Price = price,
            SequenceNumber = sequence
        };
    }

    private void SavePacketsToJson(List<StockPacket> packets)
    {
        string json = JsonConvert.SerializeObject(packets, Formatting.Indented);
        string binDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        // Navigate up to the project root directory
        string projectDirectory = Directory.GetParent(binDirectory).Parent.Parent.FullName;
        File.WriteAllText($"{projectDirectory}\\output.json", json);
        Console.WriteLine("JSON file saved: output.json");
    }
}

public class StockPacket
{
    public string Symbol { get; set; }
    public char BuySellIndicator { get; set; }
    public int Quantity { get; set; }
    public int Price { get; set; }
    public int SequenceNumber { get; set; }
}

public class Config
{
    public string ServerAddress { get; set; }
    public int ServerPort { get; set; }
}
