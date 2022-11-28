using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace FenySoft.Remote
{
  public class TTcpServer
  {
    #region Fields..

    private Thread? FWorker;
    private BlockingCollection<KeyValuePair<TServerConnection, TPacket>> FRecievedPackets;
    private readonly ConcurrentQueue<KeyValuePair<DateTime, Exception>> FErrors = new ConcurrentQueue<KeyValuePair<DateTime, Exception>>();
    private readonly ConcurrentDictionary<TServerConnection, TServerConnection> FServerConnections = new ConcurrentDictionary<TServerConnection, TServerConnection>();
    protected CancellationTokenSource FShutdownTokenSource;

    #endregion

    #region Properties..

    public int Port { get; }
    public long BytesReceive { get; internal set; }
    public long BytesSent { get; internal set; }
    public bool IsWorking => FWorker != null;
    public int ConnectionsCount => FServerConnections.Count;
    public bool IsShutdown => FShutdownTokenSource.Token.IsCancellationRequested;
    public CancellationToken ShutdownToken => FShutdownTokenSource.Token;

    #endregion

    #region Constructors..

    public TTcpServer(int port = 7182)
    {
      Port = port;
    }

    #endregion

    #region Methods..

    public void Start(int ABoundedCapacity = 64)
    {
      Stop();

      FRecievedPackets = new BlockingCollection<KeyValuePair<TServerConnection, TPacket>>(ABoundedCapacity);
      FServerConnections.Clear();

      FShutdownTokenSource = new CancellationTokenSource();

      FWorker = new Thread(DoWork);
      FWorker.Start();
    }

    public void Stop()
    {
      if (!IsWorking)
        return;

      if (FShutdownTokenSource != null)
        FShutdownTokenSource.Cancel(false);

      DisconnectConnections();

      Thread thread = FWorker;

      if (thread != null)
      {
        if (!thread.Join(5000))
          thread.Abort();
      }
    }
    
    private void DoWork()
    {
      TcpListener listener = null;

      try
      {
        listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();

        while (!FShutdownTokenSource.Token.IsCancellationRequested)
        {
          if (listener.Pending())
          {
            try
            {
              TcpClient client = listener.AcceptTcpClient();
              TServerConnection serverConnection = new TServerConnection(this, client);
              serverConnection.Connect();
            }
            catch (Exception exc)
            {
              LogError(exc);
            }
          }

          Thread.Sleep(10);
        }
      }
      catch (Exception exc)
      {
        LogError(exc);
      }
      finally
      {
        if (listener != null)
          listener.Stop();

        FWorker = null;
      }
    }

    public void LogError(Exception AException)
    {
      while (FErrors.Count > 100)
      {
        KeyValuePair<DateTime, Exception> err;
        FErrors.TryDequeue(out err);
      }

      FErrors.Enqueue(new KeyValuePair<DateTime, Exception>(DateTime.Now, AException));
    }

    private void DisconnectConnections()
    {
      foreach (var connection in FServerConnections)
        connection.Key.Disconnect();
    }

    public void ServerConnectionsAdd(TServerConnection AConnection)
    {
      FServerConnections.TryAdd(AConnection, AConnection);
    }
    
    public void ServerConnectionsRemove(TServerConnection AConnection)
    {
      TServerConnection connection;
      FServerConnections.TryRemove(AConnection, out connection);
    }
    
    public void RecievedPacketsAdd(TServerConnection AConnection, TPacket APacket)
    {
      FRecievedPackets.Add(new KeyValuePair<TServerConnection, TPacket>(AConnection, APacket));
    }
    
    // RecievedPackets.Take(ACancellationTokenSource.Token);
    public KeyValuePair<TServerConnection, TPacket> RecievedPacketsTake(CancellationToken ACancellationToken)
    {
      return FRecievedPackets.Take(ACancellationToken);
    }
    #endregion
  }
}