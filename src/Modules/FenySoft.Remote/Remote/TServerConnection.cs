using System.Collections.Concurrent;
using System.Net.Sockets;

namespace FenySoft.Remote
{
  public class TServerConnection
  {
    #region Fields..

    private Thread? FReceiver;
    private Thread? FSender;
    private volatile bool FShutdown = false;
    public BlockingCollection<TPacket> FPendingPackets;
    protected readonly TTcpServer FTcpServer;
    protected readonly TcpClient FTcpClient;

    #endregion

    #region Properties..

    public bool IsConnected { get { return FReceiver != null || FSender != null; } }

    #endregion

    #region Constructor..

    public TServerConnection(TTcpServer tcpServer, TcpClient tcpClient)
    {
      if (tcpServer == null)
        throw new ArgumentNullException("tcpServer == null");

      if (tcpClient == null)
        throw new ArgumentNullException("tcpClient == null");

      FTcpServer = tcpServer;
      FTcpClient = tcpClient;
    }

    #endregion

    public void Connect()
    {
      Disconnect();

      FTcpServer.ServerConnectionsAdd(this);
      FPendingPackets = new BlockingCollection<TPacket>();

      FShutdown = false;

      FReceiver = new Thread(DoReceive);
      FReceiver.Start();

      FSender = new Thread(DoSend);
      FSender.Start();
    }

    public void Disconnect()
    {
      if (!IsConnected)
        return;

      FShutdown = true;

      if (FTcpClient != null)
        FTcpClient.Close();

      Thread thread = FSender;

      if (thread != null && thread.ThreadState == ThreadState.Running)
      {
        if (!thread.Join(5000))
          thread.Abort();
      }

      FSender = null;

      thread = FReceiver;

      if (thread != null && thread.ThreadState == ThreadState.Running)
      {
        if (!thread.Join(5000))
          thread.Abort();
      }

      FReceiver = null;
      FPendingPackets.Dispose();
      FTcpServer.ServerConnectionsRemove(this);
    }
    
    private void DoReceive()
    {
      try
      {
        while (!FTcpServer.IsShutdown && !FShutdown && FTcpClient.Connected)
          ReceivePacket();
      }
      catch (Exception exc)
      {
        FTcpServer.LogError(exc);
      }
      finally
      {
        Disconnect();
      }
    }

    private void ReceivePacket()
    {
      BinaryReader reader = new BinaryReader(FTcpClient.GetStream());

      long id = reader.ReadInt64();
      int size = reader.ReadInt32();
      FTcpServer.BytesReceive += size;

      TPacket packet = new TPacket(new MemoryStream(reader.ReadBytes(size)));
      packet.PacketId = id;

      FTcpServer.RecievedPacketsAdd(this, packet);
    }

    private void DoSend()
    {
      try
      {
        while (!FTcpServer.IsShutdown && !FShutdown && FTcpClient.Connected)
          SendPacket();
      }
      catch (OperationCanceledException exc)
      {
      }
      catch (Exception exc)
      {
        FTcpServer.LogError(exc);
      }
      finally
      {
      }
    }

    private void SendPacket()
    {
      CancellationToken token = FTcpServer.ShutdownToken;
      TPacket packet = FPendingPackets.Take(token);
      FTcpServer.BytesSent += packet.Response.Length;
      BinaryWriter writer = new BinaryWriter(FTcpClient.GetStream());
      packet.Write(writer, packet.Response);
    }
  }
}