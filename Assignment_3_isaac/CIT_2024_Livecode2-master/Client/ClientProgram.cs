
using System.Net;
using System.Net.Sockets;
using System.Text;

var port = 5000;

var client = new TcpClient();

client.Connect(IPAddress.Loopback, port);

Console.WriteLine("Client connected!!!");

var stream = client.GetStream();

var message = "hello";

var data = Encoding.UTF8.GetBytes(message);

stream.Write(data);

var buffer = new byte[1024];

stream.Read(buffer);

var msg = Encoding.UTF8.GetString(buffer);

Console.WriteLine("Message from server: " + msg);

