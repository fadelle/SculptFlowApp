using System.Net;
using System.Net.Sockets;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>A URL the server must not fetch (private/internal destination, bad scheme or port).</summary>
public class UnsafeUrlException : Exception
{
    public UnsafeUrlException(string message) : base(message) { }
}
