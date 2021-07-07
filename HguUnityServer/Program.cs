using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace HguUnityServer
{
    class PlayerState
    {
        public float x;
        public float y;
        public DateTime lastDeltaTime = DateTime.Now;
    }
    class Program
    {
        public const int SIO_UDP_CONNRESET = -1744830452;

        static void Main(string[] args)
        {
            Console.WriteLine("HGU Unity Server Starting...");
            UdpClient udpServer = new UdpClient(20217);
            udpServer.Client.IOControl(
    (IOControlCode)SIO_UDP_CONNRESET,
    new byte[] { 0, 0, 0, 0 },
    null
);

            HashSet<IPEndPoint> endpointSet = new HashSet<IPEndPoint>();
            Dictionary<IPEndPoint, PlayerState> playerStateDict = new Dictionary<IPEndPoint, PlayerState>();

            Task.Run(async () =>
              {
                  while (true)
                  {
                      List<IPEndPoint> endpointListCopy;

                      lock (endpointSet)
                      {
                          endpointListCopy = endpointSet.ToList();
                      }

                      foreach (var ep in endpointListCopy)
                      {
                          if (playerStateDict.TryGetValue(ep, out var state))
                          {
                              var d = System.Text.Encoding.UTF8.GetBytes($"Your position: ({state.x}, {state.y})");
                              await udpServer.SendAsync(d, d.Length, ep);
                          }
                      }

                      await Task.Delay(1000);
                  }
              });

            var t = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        var recvResult = await udpServer.ReceiveAsync();
                        //Console.WriteLine($"Received data from {recvResult} (length={recvResult.Buffer.Length})");
                        lock (endpointSet)
                        {
                            endpointSet.Add(recvResult.RemoteEndPoint);
                        }
                        if (recvResult.Buffer.Length > 4
                            && recvResult.Buffer[0] == 'C'
                            && recvResult.Buffer[1] == 'H'
                            && recvResult.Buffer[2] == 'A'
                            && recvResult.Buffer[3] == 'T')
                        {
                            // 채팅 방송
                            var s = System.Text.Encoding.UTF8.GetString(recvResult.Buffer, 4, recvResult.Buffer.Length - 4);
                            foreach (var rep in endpointSet)
                            {
                                await udpServer.SendAsync(recvResult.Buffer, recvResult.Buffer.Length, rep);
                            }
                        }
                        else if (recvResult.Buffer.Length == 8)
                        {
                            // 위치 동기화
                            var dx = BitConverter.ToSingle(recvResult.Buffer);
                            var dy = BitConverter.ToSingle(recvResult.Buffer, 4);
                            if (playerStateDict.TryGetValue(recvResult.RemoteEndPoint, out var state) == false)
                            {
                                state = new PlayerState();
                                playerStateDict[recvResult.RemoteEndPoint] = state;
                            }
                            var now = DateTime.Now;
                            var totalDeltaSec = (float)(now - state.lastDeltaTime).TotalSeconds;
                            var dsq = dx * dx + dy * dy;
                            var d = (float)Math.Sqrt(dsq);
                            var moveSpeed = 10.0f;
                            if (d != 0)
                            {
                                state.x += dx / d * totalDeltaSec * moveSpeed;
                                state.y += dy / d * totalDeltaSec * moveSpeed;
                            }
                            state.lastDeltaTime = now;

                            var reply = BitConverter.GetBytes(state.x).Concat(BitConverter.GetBytes(state.y)).ToArray();
                            await udpServer.SendAsync(reply, reply.Length, recvResult.RemoteEndPoint);
                        }
                    }
                    catch (SocketException e)
                    {
                        Console.WriteLine(e);
                    }
                }
            });

            Task.WaitAll(t);
        }
    }
}
