using System.Collections.Concurrent;
using System.Net.Sockets;

namespace FenySoft.Remote
{
  public class TClientConnection
  {
    #region Fields..

    private long FPacketId = 0;

    public BlockingCollection<TPacket> PendingPackets;
    public ConcurrentDictionary<long, TPacket> SentPackets;
    private CancellationTokenSource FCancellationTokenSource;
    private Thread? FSendWorker;
    private Thread? FRecieveWorker;

    public readonly string MachineName;
    public readonly int Port;

    #endregion

    #region Properties..

    public TcpClient TcpClient { get; private set; }
    public bool IsWorking => FSendWorker != null || FRecieveWorker != null;
    private CancellationToken Shutdown => FCancellationTokenSource.Token;

    public int BoundedCapacity
    {
      get
      {
        if (!IsWorking)
          throw new Exception("Client connection is not started.");

        return PendingPackets.BoundedCapacity;
      }
    }

    #endregion

    #region Constructors..

    public TClientConnection(string AMachineName = "localhost", int APort = 7182)
    {
      MachineName = AMachineName;
      Port = APort;
    }

    #endregion

    #region Methods..

    public void Send(TPacket APacket)
    {
      if (!IsWorking)
        throw new Exception("Client connection is not started.");

      APacket.PacketId = Interlocked.Increment(ref FPacketId);
      PendingPackets.Add(APacket, FCancellationTokenSource.Token);
    }

    public void Start(int ABoundedCapacity = 64, int ARecieveTimeout = 0, int ASendTimeout = 0)
    {
      if (IsWorking)
        throw new Exception("Client connection is already started.");

      PendingPackets = new BlockingCollection<TPacket>(ABoundedCapacity);
      SentPackets = new ConcurrentDictionary<long, TPacket>();
      FCancellationTokenSource = new CancellationTokenSource();

      TcpClient = new TcpClient();
      TcpClient.ReceiveTimeout = ARecieveTimeout;
      TcpClient.SendTimeout = ASendTimeout;
      TcpClient.Connect(MachineName, Port);
      NetworkStream networkStream = TcpClient.GetStream();

      FSendWorker = new Thread(DoSend);
      FRecieveWorker = new Thread(DoRecieve);

      FSendWorker.Start(networkStream);
      FRecieveWorker.Start(networkStream);
    }

    public void Stop()
    {
      if (!IsWorking)
        return;

      FCancellationTokenSource.Cancel(false);

      Thread thread = FRecieveWorker;

      if (thread != null)
      {
        if (thread.Join(2000))
          thread.Abort();
      }

      thread = FSendWorker;

      if (thread != null)
      {
        if (thread.Join(2000))
          thread.Abort();
      }

      PendingPackets = null;
      SetException(new Exception("Client stopped"));
      FCancellationTokenSource = null;
    }
    
    private void DoSend(object AState)
    {
      BinaryWriter writer = new BinaryWriter((NetworkStream)AState);

      try
      {
        while (!Shutdown.IsCancellationRequested)
        {
          TPacket packet = PendingPackets.Take(Shutdown);
          SentPackets.TryAdd(packet.PacketId, packet);
          packet.Write(writer, packet.Request);
          writer.Flush();
        }
      }
      catch (Exception e)
      {
        SetException(e);
      }
      finally
      {
        FSendWorker = null;
      }
    }

    private void DoRecieve(object AState)
    {
      BinaryReader reader = new BinaryReader((NetworkStream)AState);

      try
      {
        while (!Shutdown.IsCancellationRequested)
        {
          long id = reader.ReadInt64();
          int size = reader.ReadInt32();
          MemoryStream response = new MemoryStream(reader.ReadBytes(size));

          if (SentPackets.TryRemove(id, out var packet))
          {
            packet.Response = response;
            packet.ResultEvent.Set();
          }
        }
      }
      catch (Exception e)
      {
        SetException(e);
      }
      finally
      {
        FRecieveWorker = null;
      }
    }

    private void SetException(Exception AException)
    {
      lock (SentPackets)
      {
        foreach (var packet in SentPackets.Values)
        {
          packet.Exception = AException;
          packet.ResultEvent.Set();
        }

        SentPackets.Clear();
      }
    }

    #endregion

  }
}