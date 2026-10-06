using System.Net;
using System.Text;
using PlasticSurgery.Business.Engines.WebScraping;

namespace PlasticSurgery.Entities.Dtos.WebScraping;

public sealed record RobotsFetchResult(RobotsTxt? Robots, bool Unavailable, string? Note);
