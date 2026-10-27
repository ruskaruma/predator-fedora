using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Avalonia.Threading;

namespace PredatorCore;

/// <summary>
///     One copy per user. A second launch (the PredatorSense key, the app menu) asks the running copy to
///     show its window over a Unix socket in $XDG_RUNTIME_DIR, then exits, so the key also works when the
///     window is hidden in the tray, minimised or behind other windows.
/// </summary>
internal static class SingleInstance
{
    private static readonly byte[] ShowRequest = "show\n"u8.ToArray();
    private static Socket? _listener;

    /// <summary>Set by the main window; runs on the UI thread for every request.</summary>
    public static Action? ShowRequested;

    private static string SocketPath =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } dir ? dir : Path.GetTempPath(),
            "predatorcore.sock");

    /// <summary>True when a running copy took the request; the caller should then exit.</summary>
    public static bool HandOffToRunningCopy()
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            socket.Send(ShowRequest);
            return true;
        }
        catch (Exception)
        {
            return false; // nobody is listening (or no usable socket path): this is the first copy
        }
    }

    public static void Listen()
    {
        try
        {
            // Only reached when nothing answered on the socket, so any file left there is stale.
            if (File.Exists(SocketPath)) File.Delete(SocketPath);
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
            _listener.Listen(4);
            new Thread(AcceptLoop) { IsBackground = true, Name = "single-instance" }.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Single-instance socket unavailable: {ex.Message}");
        }
    }

    private static void AcceptLoop()
    {
        var buffer = new byte[16];
        while (_listener != null)
        {
            try
            {
                using var client = _listener.Accept();
                client.ReceiveTimeout = 1000;
                var n = client.Receive(buffer);
                // Before the window exists (it's about to appear anyway) the dispatcher isn't touched:
                // the first thread to use it becomes the UI thread.
                if (buffer.AsSpan(0, n).SequenceEqual(ShowRequest) && ShowRequested is { } show)
                    Dispatcher.UIThread.Post(show);
            }
            catch (SocketException)
            {
                Thread.Sleep(200);
            }
        }
    }
}
