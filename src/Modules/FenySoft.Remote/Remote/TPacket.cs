namespace FenySoft.Remote
{
  ///---------------- TPacket Exchange Protocol -----------------------
  ///
  ///------------------------ Comments --------------------------------
  /// format     : binary
  /// byte style : LittleEndian
  /// PacketId   : Unique PacketId's per Connection and Unique PacketId per TPacket.
  ///
  ///-------------------------------------------------------------------
  /// TPacket    : long PacketId, int Size, byte[] buffer 
  ///  
  public class TPacket
  {
    #region Fields..

    public long PacketId;
    public readonly MemoryStream Request; // Request Message
    public MemoryStream Response;         // Response Message
    public readonly ManualResetEventSlim ResultEvent;
    public Exception Exception;

    #endregion

    #region Constructor..

    public TPacket(MemoryStream ARequest)
    {
      if (ARequest == null)
        throw new ArgumentNullException("ARequest == null");

      Request = ARequest;
      ResultEvent = new ManualResetEventSlim(false);
    }

    #endregion

    #region Methods..

    public void Wait()
    {
      ResultEvent.Wait();

      if (Exception != null)
        throw Exception;
    }

    public void Write(BinaryWriter AWriter, MemoryStream AMemoryStream)
    {
      int size = (int)AMemoryStream.Length;
      AWriter.Write(PacketId);
      AWriter.Write(size);
      AWriter.Write(AMemoryStream.GetBuffer(), 0, size);
    }

    #endregion
  }
}