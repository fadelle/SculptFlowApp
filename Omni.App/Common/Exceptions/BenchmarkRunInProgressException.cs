using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Omni.App.Common.Exceptions;

/// <summary>A run was requested while another is still pending/running for the client.</summary>
public class BenchmarkRunInProgressException : InvalidOperationException
{
    public BenchmarkRunInProgressException() : base("A benchmark run is already in progress for this client — wait for it to finish.") { }
}
