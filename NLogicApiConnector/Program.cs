using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NLogicApiConnector
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: NLogicApiConnector <request_file.json> [optional output file.csv]");
                Environment.Exit(-1);
            }

            string username = ConfigurationManager.AppSettings["Username"];
            string password = ConfigurationManager.AppSettings["Password"];

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Console.Error.WriteLine("Please specify Username and Password application settings");
                Environment.Exit(-1);
            }

            try
            {
                var request = JsonConvert.DeserializeObject<NLogic.ReportRequest>(File.ReadAllText(args[0]));

                using (var client = new NLogic.AnalyticsServiceClient())
                {
                    client.ClientCredentials.UserName.UserName = username;
                    client.ClientCredentials.UserName.Password = password;
                    var response = client.GetReport(request);

                    Stream outputStream;
                    if (args.Length >= 2)
                    {
                        Console.WriteLine($"Saving results to {args[1]}");
                        outputStream = File.Create(args[1]);
                    }
                    else
                        outputStream = Console.OpenStandardOutput();

                    using (outputStream)
                        WriteBody(request, response, outputStream);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Exception: {ex.Message}");
                Environment.Exit(-1);
            }
        }

        private static void WriteBody(NLogic.ReportRequest request, NLogic.ReportResponse response, Stream output)
        {
            using (var tw = new StreamWriter(output, Encoding.GetEncoding(1252), 1024, true))
            {
                tw.Write("Market,Audience,");

                switch (request.Type)
                {
                    case NLogic.ReportType.Program:
                        tw.Write("ProgramName,ProgramCode,EpisodeTitle,Station,");

                        if (request.GroupByFields != null && request.GroupByFields.Length > 0)
                            // crosstab
                            tw.Write("FirstAirDate,LastAirDate,NumAired,");
                        else
                            // episode list
                            tw.Write("EpisodeAirDate,");

                        tw.Write("Weekdays,StartTime,EndTime");
                        break;
                    case NLogic.ReportType.Daypart:
                        tw.Write("Station,Daypart");
                        break;
                    default:
                        throw new NotImplementedException();
                }

                foreach (var stat in request.RequestedStats)
                    tw.Write("," + stat.ToString());

                tw.WriteLine();

                var statProperties = typeof(NLogic.Stats).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(a => {
                        NLogic.StatType statType;
                        if (Enum.TryParse<NLogic.StatType>(a.Name, out statType))
                            return new { statType, property = a };
                        else
                            return null;
                    })
                    .Where(a => a != null)
                    .ToDictionary(a => a.statType, a => a.property);

                foreach (var row in response.Results)
                {
                    for (int marketIndex = 0; marketIndex < response.Markets.Length; marketIndex++)
                    {
                        for (int audienceIndex = 0; audienceIndex < response.AudienceInformation.Length; audienceIndex++)
                        {
                            tw.Write(response.Markets[marketIndex].Name);
                            tw.Write(",");

                            tw.Write(response.AudienceInformation[audienceIndex].Label);
                            tw.Write(",");

                            switch (request.Type)
                            {
                                case NLogic.ReportType.Program:
                                    tw.Write($"{row.ProgramName},{row.ProgramCode},{row.EpisodeTitle},{row.StationName},");

                                    if (request.GroupByFields != null && request.GroupByFields.Length > 0)
                                        // crosstab
                                        tw.Write($"{row.FirstAirDate:yyyy-MM-dd},{row.LastAirDate:yyyy-MM-dd},{row.NumAired},");
                                    else
                                        // plain episodes
                                        tw.Write($"{row.EpisodeAirDate:yyyy-MM-dd},");
                                    
                                    tw.Write($"{row.Weekdays},{row.StartTime},{row.EndTime}");
                                    break;
                                case NLogic.ReportType.Daypart:
                                    tw.Write($"{row.StationName},{row.DaypartLabel}");
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }

                            var rowValues = row.StatsPerAudience[audienceIndex].PerMarket[marketIndex];
                            foreach (var stat in request.RequestedStats)
                                tw.Write($",{statProperties[stat].GetValue(rowValues)}");

                            tw.WriteLine();
                        }
                    }
                }
            }
        }
    }
}
