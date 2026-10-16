using System.Net;
using System.Text;
using Omni.App.Business.Engines.WebScraping;

namespace Omni.App.Entities.Dtos.WebScraping;

public sealed record RobotsFetchResult(RobotsTxt? Robots, bool Unavailable, string? Note);
