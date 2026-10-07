using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>A run was requested while another is still pending/running for the clinic.</summary>
public class BenchmarkRunInProgressException : InvalidOperationException
{
    public BenchmarkRunInProgressException() : base("A benchmark run is already in progress for this clinic — wait for it to finish.") { }
}
