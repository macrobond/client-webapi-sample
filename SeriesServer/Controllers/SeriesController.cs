// Macrobond Financial AB 2020-2023

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

#nullable enable


// This is a very basic sample implementation of a server that can be called from the Macrobond application
// to retrieve series. It uses a simple in-memory list of series.
// The sample server supports a browse tree and also support removing and editing series.

namespace SeriesServer.Controllers
{
#if USEAUTHENTICATION
    [Authorize]
#endif
    [Produces("application/json")]
    [Route("")]
    [ApiController]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class SeriesController : ControllerBase
    {
        public SeriesController()
        {
        }

        private static bool ImplementsEditSeries => true;

        private static bool ImplementsBrowse => true;

        private static bool ImplementsSearch => true;

        private static bool AllowMultipleSeriesPerRequest => true;

        private static bool ImplementsMeta => true;

        private static bool ImplementsRevisions => true;

        private static bool ImplementsRevisionsRelease => true;

        private static bool ImplementsRevisionsCompleteHistory => true;

        /// <summary>
        /// Declare if this server support Browse, Search and Edit. This will enable features in the client application.
        /// </summary>
        /// <remarks>This method must always be implemented.</remarks>
        [HttpGet("getcapabilities")]
        public ActionResult<Implements> GetCapabilities()
        {
            return new Implements(ImplementsBrowse, ImplementsSearch, ImplementsEditSeries, AllowMultipleSeriesPerRequest, ImplementsMeta, ImplementsRevisions, ImplementsRevisionsRelease, ImplementsRevisionsCompleteHistory);
        }

        /// <summary>
        /// Returns metadata for one or more series identified by a list of names.
        /// </summary>
        /// <param name="names">Names of series to find.</param>
        /// <returns>List of metadata for series.</returns>
        /// <remarks>This method will only ever be called if the server has returned Meta capability in GetCapabilities</remarks>
        [HttpGet("loadmeta")]
        public ActionResult<List<DownloadResult<Entity>>> LoadMeta([Required][FromQuery(Name = "n")] string[] names)
        {
            var listToReturn = names.Select(LoadSeriesInternal).ToList();

            if (listToReturn.Any())
                return listToReturn;

            return NotFound();

            DownloadResult<Entity> LoadSeriesInternal(string name)
            {
                Series? series = LoadSeriesCore(name);
                if (series == null)
                    return new DownloadResult<Entity>("Series could not be found!");
                return new DownloadResult<Entity>(new Entity(series.MetaData));
            }
        }

        /// <summary>
        /// Returns one or more series identified by a list of names.
        /// </summary>
        /// <param name="names">Names of series to find.</param>
        /// <returns>List of series.</returns>
        /// <remarks>This method must always be implemented.</remarks>
        [HttpGet("loadseries")]
        public ActionResult<List<DownloadResult<Series>>> LoadSeries([Required][FromQuery(Name = "n")] string[] names)
        {
            var listToReturn = names.Select(LoadSeriesInternal).ToList();

            if (listToReturn.Any())
                return listToReturn;

            return NotFound();

            DownloadResult<Series> LoadSeriesInternal(string name)
            {
                Series? series = LoadSeriesCore(name);

                if (series == null)
                    return new DownloadResult<Series>("Series could not be found!");
                // series.Values = series.Values.Reverse().ToArray();
                // series.MetaData[LastModifiedTimeStamp] = DateTime.Now;
                return new DownloadResult<Series>(series);
            }
        }

        /// <summary>
        /// Returns one or more series identified by a list of names.
        /// </summary>
        /// <param name="name">Name of series to find.</param>
        /// <param name="timestamp">The vintage timestamp.</param>
        /// <returns>List of series.</returns>
        /// <remarks>This method will only ever be called if the server has returned Revisions capability in GetCapabalities</remarks>
        [HttpGet("loadvintage")]
        public ActionResult<Series> LoadVintage([Required][FromQuery(Name = "n")] string name, [Required][FromQuery] DateTimeOffset timestamp)
        {
            if (name == "withrev")
            {
                var result = WithRevSeriesVintages.OrderBy(p => p.Vintage).Where(p => p.Vintage <= timestamp).LastOrDefault();
                if (result.Series == null)
                    result = WithRevSeriesVintages[0];

                var meta = new Dictionary<string, object>(WithRevMetaData)
                {
                    { "RevisionSeriesType", "vintage" },
                    { "RevisionTimeStamp", result.Vintage },
                };

                return result.Series with { MetaData = meta };
            }

            return NotFound();
        }

        /// <summary>
        /// Returns a list of vintage timestamps with an optional label for each vintage.
        /// </summary>
        /// <param name="name">Name of series to find.</param>
        /// <returns>List of vintages.</returns>
        /// <remarks>This method will only ever be called if the server has returned Revisions capability in GetCapabalities</remarks>
        [HttpGet("loadvintagetimestamps")]
        public ActionResult<List<Vintage>> LoadVintageTimestamps([Required][FromQuery(Name = "n")] string name)
        {
            if (name == "withrev")
            {
                return WithRevSeriesVintages.Select(p => new Vintage(p.Vintage, p.Label)).ToList();
            }

            return NotFound();
        }

        [HttpGet("loadrelease")]
        public ActionResult<Series> LoadRelease([Required][FromQuery(Name = "n")] string name, [Required][FromQuery] int nthrelease)
        {
            if (name == "withrev")
            {
                var result = nthrelease < WithRevSeriesReleases.Count ? WithRevSeriesReleases[nthrelease] : WithRevSeriesReleases[^1];

                var meta = new Dictionary<string, object>(WithRevMetaData)
                {
                    { "RevisionSeriesType", "nth" },
                    { "RevisionSeriesNth", nthrelease },
                };

                return result with { MetaData = meta };
            }

            return NotFound();
        }

        [HttpGet("loadcompletehistory")]
        public ActionResult<Dictionary<DateTime, Series>> LoadCompleteHistory([Required][FromQuery(Name = "n")] string name)
        {
            if (name == "withrev")
            {
                Dictionary<DateTime, Series> result = new();
                foreach (var (vintage, _, series) in WithRevSeriesVintages)
                {
                    var meta = new Dictionary<string, object>(WithRevMetaData)
                    {
                        { "RevisionSeriesType", "vintage" },
                        { "RevisionTimeStamp", vintage },
                    };

                    foreach (var (k, v) in series.MetaData)
                        meta[k] = v;

                    result[vintage] = series with { MetaData = meta };
                }

                return result;
            }

            return NotFound();
        }

        /// <summary>
        /// Create or replace a series.
        /// </summary>
        /// <param name="series">The series data including values, dates and metadat.</param>
        /// <param name="lastModified">Included when a series is replaced. This is the timestamp returned from the previous call to this method or LoadSeries. Typically the save operation fails if this does not match the stored series.</param>
        /// <param name="forceReplace">Replace series even if lastModified timestamp is not specified or not matching.</param>
        /// <returns>The timestamp when the series was stored.</returns>
        /// <remarks>This method will only ever be called if the server has returned EditSeries capability in GetCapabalities</remarks>
        [HttpPost("createseries")]
        public ActionResult<DateTime> CreateSeries([FromBody] Series series, [FromQuery] DateTime? lastModified, [FromQuery] bool? forceReplace)
        {
            Dictionary<string, object> meta = DeserializeMetaData(series);
            List<object> values = DeserializeValues(series);

            Series newSeries = new Series(meta, values.ToArray(), series.Dates);

            string name = (string)meta["PrimName"];

            if (name == "withrev")
                return BadRequest();

            var now = DateTime.Now;
            meta[LastModifiedTimeStamp] = now;

            lock (Lock)
            {
                int index = Database.FindIndex(s => (string)s.MetaData["PrimName"] == name);

                if (index >= 0)
                {
                    // This series is already in the database. Check that it matches the same timestamp of the input series.
                    var item = Database[index];
                    if ((string)item.MetaData["PrimName"] == name)
                    {
                        if (forceReplace == true)
                        {
                            Database[index] = newSeries;
                            return Ok(now);
                        }

                        if (lastModified is null)
                            return Conflict("Series with that name already exists");

                        var oldDt = (DateTime)item.MetaData[LastModifiedTimeStamp];
                        if (lastModified == oldDt)
                        {
                            Database[index] = newSeries;
                            return Ok(now);
                        }
                        else
                            return Conflict("LastModified does not match");
                    }
                }

                if (lastModified != null || forceReplace == true)
                    NotFound();

                // Just add to the list returned when browsing

                Database.Add(newSeries);
                foreach (var p in SeriesMetaData)
                {
                    if (p.Key == "Other")
                    {
                        var seriesRowList = p.Value.Groups[0].Series.ToList();
                        seriesRowList.Add(new SeriesRow(name, 0, false, false, new[] { name }));
                        p.Value.Groups[0].Series = seriesRowList.ToArray();
                    }
                }
            }

            return Ok(now);
        }

        /// <summary>
        /// Removes a series from the database.
        /// </summary>
        /// <param name="name">Names of the series to be removed.</param>
        /// <returns>Returns 404 if series could not be found.</returns>
        /// <remarks>This method will only ever be called if the server has returned EditSeries capability in GetCapabalities</remarks>
        [HttpGet("removeseries")]
        public ActionResult RemoveSeries([Required][FromQuery(Name = "n")] string name)
        {
            bool found;
            lock (Lock)
            {
                int index = Database.FindIndex(s => name == (string)s.MetaData["PrimName"]);

                if (index >= 0)
                {
                    Database.RemoveAt(index);
                    found = true;
                }
                else
                    found = false;

                // Also remove from our internal list of series

                foreach (var p in SeriesMetaData)
                {
                    foreach (var g in p.Value.Groups)
                    {
                        (int x, int y)? removeAt = null;
                        for (int x = 0; x < g.Series.Length; x++)
                        {
                            for (int y = 0; y < g.Series[x].Names.Length; y++)
                            {
                                if (g.Series[x].Names[y] == name)
                                    removeAt = (x, y);
                            }
                        }

                        if (removeAt != null)
                        {
                            var list = g.Series[removeAt.Value.x].Names.ToList();
                            list.RemoveAt(removeAt.Value.y);
                            g.Series[removeAt.Value.x].Names = list.ToArray();
                            if (g.Series[removeAt.Value.x].Names.Length == 0)
                            {
                                var sList = g.Series.ToList();
                                sList.RemoveAt(removeAt.Value.x);
                                g.Series = sList.ToArray();
                            }
                        }
                    }
                }
            }

            if (found)
                return Ok();
            return NotFound();
        }

        /// <summary>
        /// Used for getting LastModified from series when editing. 
        /// </summary>
        private readonly string LastModifiedTimeStamp = "LastModifiedTimeStamp";

        /// <summary>
        /// Helper function for getting series from internal memory db.
        /// </summary>
        /// <param name="name">Name of series.</param>
        /// <returns>Requested series of null if no series with that name could be found.</returns>
        private Series? LoadSeriesCore(string name)
        {
            lock (Lock)
            {
                return name switch
                {
                    "withrev" => WithRevSeriesVintages[^1].Series with { MetaData = WithRevMetaData },
                    _ => Database.FirstOrDefault(s => string.Compare((string)s.MetaData["PrimName"], name, StringComparison.OrdinalIgnoreCase) == 0),
                };
            }
        }

        /// <summary>
        /// Loads part of or a complete database tree.
        /// </summary>
        /// <param name="reference">Loads the part of the tree referenced, or the root of the tree if no reference is specified.</param>
        /// <returns>A list of tree nodes. A node may contain nested nodes, nested node branch references or nested series list references.</returns>
        /// <remarks>This method will only ever be called if the server has returned Browse capability in GetCapabalities</remarks>
        [HttpGet("loadtree")]
        public ActionResult<List<object>> LoadTree([FromQuery] string? reference)
        {
            lock (Lock)
            {
                if (string.IsNullOrWhiteSpace(reference))
                    return BrowseDataBase[string.Empty];

                if (BrowseDataBase.TryGetValue(reference, out List<object>? value))
                    return value;
            }

            return NotFound();
        }


        /// <summary>
        /// Lists all series in the current leaf node.
        /// </summary>
        /// <param name="reference">Name of the branch to list from.</param>
        /// <remarks>This method will only ever be called if the server has returned Browse capability in GetCapabalities</remarks>
        [ProducesResponseType(typeof(SeriesList), (int)HttpStatusCode.OK)]
        [HttpGet("listseries")]
        public ActionResult<SeriesList> ListSeries([Required][FromQuery] string reference)
        {
            lock (Lock)
            {
                if (!string.IsNullOrWhiteSpace(reference) && SeriesMetaData.TryGetValue(reference, out SeriesList? value))
                {
                    foreach (var group in value.Groups)
                    {
                        foreach (var seriesRow in group.Series)
                        {
                            var series = new List<Series>(seriesRow.Names.Length);
                            foreach (var entityName in seriesRow.Names)
                                series.Add(LoadSeriesCore(entityName)!);

                            seriesRow.EntityMeta = series.Select(x => x is null || x.MetaData is null ? null : x.MetaData).ToArray(); ;
                        }
                    }
                    return value;
                }
            }

            return NotFound();
        }


        /// <summary>
        /// Finds and returns a time series from the server by text search.
        /// </summary>
        /// <remarks>This method will only ever be called if the server has returned Search capability in GetCapabalities</remarks>
        [HttpGet("searchseries")]
        public ActionResult<IEnumerable<Dictionary<string, object>>> SearchSeries([Required][FromQuery] string query)
        {
            // In this sample code we just look for matching text in the description of the series. What the query means is up to the implementation, but it is typically interpreted as a set of words to search for.

            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();

            lock (Lock)
            {
                foreach (var result in Database)
                {
                    if (result.MetaData["Description"] is string name && name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        list.Add(result.MetaData);
                }
            }

            if (list.Count != 0)
                return list;

            return NotFound();
        }

        private static List<object> DeserializeValues(Series series)
        {
            List<object> values = new List<object>();
            foreach (var x in series.Values)
            {
                object value = x switch
                {
                    JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetDouble(out var d) => d,
                    JsonElement { ValueKind: JsonValueKind.String } e when e.GetString()?.Equals("NaN", StringComparison.OrdinalIgnoreCase) == true => double.NaN,
                    _ => throw new ArgumentException("Invalid value in Series Values.", nameof(series)),
                };

                values.Add(value);
            }

            return values;
        }

        private static Dictionary<string, object> DeserializeMetaData(Series series)
        {
            Dictionary<string, object> meta = new Dictionary<string, object>();
            foreach (var x in series.MetaData)
            {
                var jsonElementValue = (JsonElement)x.Value;

                object val;

                if (jsonElementValue.ValueKind == JsonValueKind.Number)
                {
                    if (jsonElementValue.TryGetInt32(out int i))
                        val = i;
                    else if (jsonElementValue.TryGetDouble(out double d))
                        val = d;
                    else if (jsonElementValue.TryGetInt64(out long l))
                        val = l;
                    else
                        val = jsonElementValue.ToString();
                }
                else if (jsonElementValue.ValueKind == JsonValueKind.Array)
                {
                    var enumerator = jsonElementValue.EnumerateArray();
                    val = enumerator.Select(x => x.ToString());
                }
                else
                {
                    string str = jsonElementValue.ToString();

                    if (DateTime.TryParse(str, out DateTime dt))
                        val = dt;
                    else
                        val = str;
                }

                meta.Add(x.Key, val);
            }

            return meta;
        }

        public sealed record Entity(Dictionary<string, object> MetaData);

        /// <summary>
        /// Represents a time series object
        /// </summary>
        public sealed record Series([property: Required] Dictionary<string, object> MetaData, [property: Required] object[] Values, DateTime[]? Dates = null, Dictionary<string, object>?[]? PerValueMetaData = null);

        /// <summary>
        /// Represents a list of time series result that is displayed in the data browser of the Macrobond application
        /// when selecting a node in the data tree.
        /// </summary>
        public sealed record SeriesList(Aspect[]? Aspects, [property: Required] Group[] Groups);

        /// <summary>
        /// Represent an aspect tab the list of series that is displayed in the data browser of the Macrobond application
        /// when selecting a node in the data tree.
        /// </summary>
        public sealed record Aspect(string Name, string Description);

        /// <summary>
        /// Represent an group in the list of series that is displayed in the data browser of the Macrobond application
        /// when selecting a node in the data tree.
        /// </summary>
        public sealed class Group
        {
            public string Name { get; set; }

            /// <summary>
            /// A list of series rows.
            /// </summary>
            [Required]
            public SeriesRow[] Series { get; set; }

            public Group(string name, SeriesRow[] series)
            {
                Name = name;
                Series = series;
            }
        }

        /// <summary>
        /// A class that holds display options for a row of series as well as the MetaData of the series themselves.
        /// This is displayed in the data browser of the Macrobond application when selecting a node in the data tree.
        /// </summary>
        public sealed class SeriesRow
        {
            public string Description { get; set; }

            public int? Indentation { get; set; }

            public bool? Emphasized { get; set; }

            public bool? SpaceAbove { get; set; }

            /// <summary>
            /// A dictionary of strings and object that holds the meta data of the time series in this row.
            /// </summary>
            [Required]
            public IList<Dictionary<string, object>?> EntityMeta { get; set; } = null!;

            public string[] Names { get; set; }

            public SeriesRow(string description, int? indentation, bool? emphasized, bool? spaceAbove, string[] names)
            {
                Description = description;
                Indentation = indentation;
                Emphasized = emphasized;
                SpaceAbove = spaceAbove;
                // EntityMeta = entityMeta;
                Names = names;
            }
        }

        /// <summary>
        /// Wraps a series so that the server can return a result.
        /// The result should be either a series in the Data field _or_ an error message in the Error field.
        /// </summary>
        public record DownloadResult<T>(T? Data, string? Error = null)
            where T : class
        {
            public DownloadResult(string error)
                : this(null, error)
            {
            }
        }

        public record Vintage(DateTime TimeStamp, string? Label);

        // Tells the client if this server has search and browse capabilities.
        public record struct Implements(bool Browse, bool Search, bool EditSeries, bool AllowMultipleSeriesPerRequest, bool Meta, bool Revisions, bool RevisionsRelease, bool RevisionsCompleteHistory);

        private static readonly object Lock = new();

        // Sample data

        private static readonly List<Series> Database = new List<Series>
        {
            new Series
            (
                new Dictionary<string, object>()
                {
                    { "Description", "Arrivals, Total" },
                    { "Region", new string[] { "pl", "se"} },
                    { "StartDate", new DateTime(2003, 01, 01, 0, 0, 0) },
                    { "Frequency", "monthly" },
                    { "PrimName", "pltour0001" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    4319590.0d, 4059520.0d, 3605839.0d, 3280645.0d, 2460560.0d, 2401899.0d, 2187680.0d, 2124118.0d, 2194190.0d, 2436125.0d, 2863790.0d, 3267168.0d, 4067560.0d, 3926772.0d, 3389729.0d,
                    3012555.0d, 2467756.0d, 2205113.0d, 2045505.0d, 2019667.0d, 2116904.0d, 2273641.0d, 2671256.0d, 3123243.0d, 3803479.0d, 3755086.0d, 3250518.0d, 2847226.0d, 2285368.0d, 2107293.0d,
                    1907901.0d, 1847429.0d, 1925399.0d, 2086069.0d, 2518407.0d, 2988738.0d, 3673076.0d, 3562000.0d, 3021650.0d, 2692655.0d, 2238153.0d, 1868981.0d, 1820325.0d, 1712855.0d, 1703224.0d,
                    1859672.0d, 2298723.0d, 2564382.0d, 3290501.0d, 3213236.0d, 2751569.0d, 2486087.0d, 1886291.0d, 1751285.0d, 1335696.0d, 1518140.0d, 1575546.0d, 1770903.0d, 2200460.0d, 2422058.0d,
                    3023564.0d, 2888982.0d, 2559119.0d, 2393934.0d, 1706223.0d, 1656789.0d, 1471216.0d, 1415184.0d, 1449718.0d, 1644771.0d, 2032574.0d, 2242779.0d, 2831122.0d, 2760470.0d, 2395297.0d,
                    2171759.0d, 1665928.0d, 1490075.0d, 1364136.0d, 1352509.0d, 1358351.0d, 1561283.0d, 1930419.0d, 2243597.0d, 2661899.0d, 2651820.0d, 2153083.0d, 2147447.0d, 1639105.0d, 1567050.0d,
                    1360374.0d, 1360960.0d, 1309922.0d, 1542736.0d, 1857400.0d, 2158102.0d, 2465980.0d, 2472530.0d, 2254963.0d, 2006688.0d, 1510034.0d, 1449978.0d, 1232497.0d, 1215786.0d, 1239534.0d,
                    1431579.0d, 1784893.0d, 2049291.0d, 2372180.0d, 2400058.0d, 2116598.0d, 1938052.0d, 1400013.0d, 1365083.0d, 1202283.0d, 1161932.0d, 1153165.0d, 1297513.0d, 1662252.0d, 1910860.0d,
                    2346580.0d, 2271978.0d, 1963040.0d, 1869483.0d, 1337026.0d, 1263689.0d, 1161222.0d, 1116904.0d, 1116818.0d, 1323066.0d, 1657795.0d, 1885828.0d, 2243946.0d, 2232365.0d, 2057313.0d,
                    1974376.0d, 1492336.0d, 1225430.0d, 1198718.0d, 1148111.0d, 1129449.0d, 1318365.0d, 1661851.0d, 1860727.0d, 2184798.0d, 2190963.0d, 2025256.0d, 1830046.0d, 1337853.0d, 1267165.0d,
                    1071763.0d, 1068924.0d, 1053084.0d, 1179641.0d, 1529791.0d, 1775256.0d, 2068282.0d, 2125748.0d, 1864524.0d, 1629537.0d, 1208431.0d, 1142133.0d, 972329.0d, 963359.0d, 933081.0d,
                    1082660.0d, 1366319.0d, 1660568.0d, 2043446.0d, 2065591.0d, 1826157.0d, 1621937.0d, 1148687.0d, 988491.0d, 896102.0d, 920212.0d, 906420.0d, 992996.0d, 1324986.0d, 1561451.0d,
                    1998578.0d, 1953224.0d, 1735239.0d, 1552542.0d, 1048675.0d, 977898.0d, 871403.0d, 822279.0d, 810468.0d, 914851.0d, 1206840.0d, 1456858.0d, 1974578.0d, 1873718.0d, 1536640.0d,
                    1524528.0d, 929582.0d, 866119.0d, 782911.0d, 767161.0d,
                }
            ),
            new Series
            (
                new Dictionary<string, object>()
                {
                    { "Description", "Domestic Trade, Wholesale Trade" },
                    { "Region", "pl" },
                    { "StartDate", new DateTime(2004, 11, 01, 0, 0, 0) },
                    { "Frequency", "monthly" },
                    { "PrimName", "pltrad0021" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    105.6, 99.8, 106.1, 93.9, 107.3, 109, 104.1, 107.2, 105.3, 99.2, 98.3, 103.9, 98.3, 103.1, 105.5, 105.6, 101, 104.8, 96.6, 101.7, 103.2, 97.5, 106, 110.3, 103.5, 103.4, 104.2,
                    100.8, 109.8, 97.8, 109.5, 99.5, 113.1, 105.5, 108.5, 102.6, 104.8, 114.4, 103, 115.6, 109.1, 106.8, 102.6, 104.4, 95.2, 100.1, 101.7, 93.9, 98, 93.6, 94.8, 93.4, 86, 100.4, 106.7,
                    106.1, 102.1, 107.8, 105, 108.5, 110.3, 108.1, 109.5, 110, 119.2, 111.1, 111.1, 109.4, 109.7, 107.7, 96.4, 101.6, 104.9, 96.1, 101.9, 100.5, 92.9, 101.7, 92.9, 97.6, 97.2, 95.3,
                    104.3, 111.9, 97.9, 108.5, 113.5, 107.8, 114.7, 110.2, 108.4, 114.8, 121.9, 94.6, 92.7, 89.8, 94, 90.9, 85.3, 90.3, 95.7, 89.1, 90.6, 88.9, 85.8, 111.7, 118.1, 108.7, 111.4, 119.3,
                    113.2, 110.4, 109.1, 103, 102.8, 102, 105.5, 97.2, 97, 90.5, 94.1, 94.7, 92.1, 91.1, 87.9, 89.8, 101.1, 88.8, 86.5, 103.7, 93.7, 105, 106.8, 92.4, 106.4, 105.9, 105.1, 113.8,
                    101.8, 120.8, 108.1, 96.8, 105.8, 110.9, 103.1, 107, 109.9, 104.8, 103.5, 107.5, 111.9, 111.7, 118.5, 101.4, 109.1, 114.6, 107.2, 106.7, 105.9, 110, 114.3, 101.6, 109.1, 101.8,
                    99.8, 103.3, 101.8, 94.8, 95.5, 98.4, 93, 97.3, 95.4, 86.8, 88.4, 96.3, 99.9, 98.6, 100.8,
                }
            ),
            new Series
            (
                new Dictionary<string, object>()
                {
                    { "Description", "Domestic Trade, Food" },
                    { "Region", "pl" },
                    { "StartDate", new DateTime(2002, 01, 01, 0, 0, 0) },
                    { "Frequency", "monthly" },
                    { "PrimName", "pltrad0014" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    117.1, 112.3, 116.3, 104.2, 110.9, 121.6, 99.8, 109.4, 108.7, 105.5, 108.8, 114.2, 104.6, 109.5, 107.7, 105.4, 108.8, 107.9, 108.6, 111, 112.2, 110.9, 120, 121, 117.8, 124.2, 124,
                    128.5, 127.2, 124.2, 126.9, 116.6, 129, 107.9, 112.6, 106.2, 106.7, 108.3, 101, 103.6, 104.1, 103.7, 101.7, 107.6, 99.2, 108.1, 108.4, 103.8, 110.9, 98.3, 94.4, 97.5, 95.3, 89.6,
                    101.6, 94.3, 95.5, 97.6, 90.4, 92.9, 96.7, 103.8, 102.8, 103.8, 102, 107.9, 102.8, 105.6, 100.3, 106.9, 99.2, 101.7, 106.1, 99.3, 109, 108.5, 104, 106.5, 102.5, 104.6, 108.4, 94.5,
                    104.8, 111.6, 96.7, 102.3, 104.1, 100.3, 105.4, 96.5, 105.2, 106.5, 107.9, 106.5, 105.8, 107.7, 104.8, 103.8, 96.8, 98.9, 105.1, 114.8, 101.4, 105, 106.3, 110, 106.6, 100.4, 109,
                    109.9, 104.5, 106.9, 106.3, 92.3, 106.2, 101.9, 104.3, 100.2, 104.7, 105.5, 110.2, 114.1, 116.1, 115, 110.9, 113.8, 117.5, 101.1, 97.4, 139.1, 121.9, 121.1, 120.4, 108.9, 119.6,
                    121.5, 114.7, 129.4, 117.2, 131.9, 127.8, 112.3, 116.3, 118.9, 113.1, 114.2, 98.9, 111.7, 118.8, 117.3, 114.1, 112, 117, 110.6, 112.2, 116.6, 109.5, 110.4, 132.8, 107.1, 111.9,
                    112.8, 111.8, 111.9, 109, 107.5, 107, 105.7, 107.7, 111.2, 102.7, 106, 108.7, 101.8, 102.8, 107.4, 102.2, 103.2, 114.7, 104.4, 110.4, 114.1, 110.1, 117.8, 106.8, 100.8, 117.1,
                    104.3, 104.3, 103, 93.9, 97.6, 99.1, 88.3, 91.6, 92.2, 90.3, 98.1, 84.3, 89.2, 85.5, 93.5, 92.9, 90.4, 95.2, 93.7, 96.4, 97.3, 96.4, 95.4, 103.7, 109.2, 107.9,
                }
            ),
            new Series
            (
                new Dictionary<string, object>()
                {
                    { "Description", "Domestic Trade, New Passenger Car" },
                    { "Region", "pl" },
                    { "StartDate", new DateTime(2003, 01, 01, 0, 0, 0) },
                    { "Frequency", "monthly" },
                    { "PrimName", "ecb_stsmplwcregpc00003abs" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    36179, 48945, 48034, 48014, 46473, 46133, 52224, 44408, 45141, 48248, 41836, 39173, 28955, 55731, 46904, 45846, 43263, 46041, 52013, 42755, 45292, 51034, 40990, 41158, 38009,
                    33915, 38634, 41668, 39192, 42074, 48070, 38992, 38739, 43374, 37240, 33914, 32308, 27838, 32792, 37050, 33676, 34334, 39490, 32999, 34662, 35804, 30355, 29529, 26967, 23969,
                    29031, 30229, 27770, 28375, 33825, 28814, 31250, 29420, 25210, 28162, 23316, 21066, 25029, 26511, 24171, 27896, 37353, 33072, 29689, 27146, 24971, 25883, 22151, 19399, 24313,
                    25950, 23032, 23888, 26432, 24464, 25797, 22393, 22390, 21701, 19988, 17697, 21108, 24020, 23716, 24612, 29827, 23478, 24708, 27107, 24942, 23822, 20913, 19262, 24267, 24603,
                    22159, 23063, 26539, 22226, 20953, 39965, 31401, 28745, 23204, 22212, 24817, 27087, 24601, 23504, 28588, 21938, 23164, 28270, 26998, 25650, 24608, 22179, 24141, 26689, 25870,
                    28584, 31314, 30643, 27124, 29423, 29103, 27125, 22865, 21493, 22967, 27361, 27657, 26961, 32776, 27607, 27889, 28422, 23736, 24193, 24122, 20506, 24496, 26554, 25421, 26057,
                    28134, 22342, 22380, 22940, 21300, 21210, 19559, 17146, 19910, 20867, 19490, 21168, 21031, 18421, 18360, 18532, 17144, 16565, 16999, 17462, 20570, 22290, 21351, 19913, 24081,
                    20118, 23409, 21827, 19919, 20693, 20343, 19908, 22394, 23851, 26596, 44830, 38025, 30656, 29655, 37373, 32905, 31493, 30380, 25319, 28841, 30790, 31650, 31162, 29119, 25817,
                    27715,
                }
            ),
            new Series
            (
                new Dictionary<string, object>()
                {
                    { "Description", "Foreign Trade, Export, United States" },
                    { "Region", "pl" },
                    { "StartDate", new DateTime(2005, 01, 01, 0, 0, 0) },
                    { "Frequency", "annual" },
                    { "PrimName", "pltrad0135" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    26200000000, 23367941400, 18859110800, 16841434600, 15174652800, 15239242500, 11754934900, 10879093500, 8761157500, 7639377800, 5907934000, 5726277300, 6568862600, 5974127000,
                }
            ),
            new Series
            (
                new Dictionary<string, object>()
                {
                    { "Description", "Foreign Trade, Import, United States" },
                    { "Region", "pl" },
                    { "StartDate", new DateTime(2005, 01, 01, 0, 0, 0) },
                    { "Frequency", "annual" },
                    { "PrimName", "pltrad0131" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    27600000000, 25027500000, 22135300000, 19773500000, 17168200000, 17430700000, 16579900000, 14139100000, 13560300000, 10722000000, 10975000000, 9621600000, 8688200000, 7819500000,
                }
            ),
            new Series
            (
                new Dictionary<string, object>()
                {
                    { "Description", "Foreign Trade, Trade Balance" },
                    { "Region", "pl" },
                    { "StartDate", new DateTime(1996, 01, 01, 0, 0, 0) },
                    { "Frequency", "monthly" },
                    { "PrimName", "pltrad0051" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    139339400000, 122301700000, 104821000000, 88632500000, 72138400000, 54068200000, 35329100000, 18879900000, 188848100000, 177674900000, 156903400000, 136973300000, 118474100000,
                    103487700000, 88747800000, 74297800000, 58215300000, 42986600000, 29336400000, 15848200000, 168644000000, 160453000000, 144431700000, 128103300000, 111922200000, 97781700000,
                    86655400000, 74164000000, 60492000000, 45577500000, 29767200000, 16367800000, 156939600000, 144658900000, 129372100000, 115541200000, 102080400000, 90591700000, 80197800000,
                    65923800000, 53925100000, 43420600000, 29183500000, 15152400000, 148294700000, 138014800000, 124205100000, 110805300000, 96626600000, 85165700000, 74776600000, 62717700000,
                    51239000000, 39050000000, 26920600000, 14785500000, 118784200000, 110879600000, 101045500000, 89800500000, 77871000000, 68206000000, 59846600000, 51005100000, 41608800000,
                    30933100000, 21678500000, 12382500000, 97873500000, 92418100000, 83480000000, 73996500000, 63206200000, 55272700000, 47841100000, 39082800000, 32276600000, 22524100000,
                    16320300000, 9397300000, 77996400000, 74777500000, 67790800000, 58196300000, 50153700000, 42544800000, 36773700000, 29853900000, 23853800000, 18574500000, 13194500000, 7979700000,
                    54923100000, 51280500000, 45464300000, 38969200000, 31831400000, 27233600000, 25037700000, 21818700000, 18214200000, 14829900000, 10607500000, 6010500000, 48053700000, 45976100000,
                    42533400000, 38628400000, 34119000000, 31160300000, 28206900000, 23868000000, 18472600000, 12383100000, 9623100000, 4779600000, 41901500000, 38242100000, 33746200000, 29098900000,
                    24667800000, 22345200000, 19719200000, 16964300000, 13958200000, 11236500000, 8258600000, 3856300000, -7837000000, -5132000000, -5213900000, -5372100000, -5188700000, -4730900000,
                    -2630200000, -1155300000, 1504000000, 1818800000, 2388000000, 1829000000, 3214400000, 4785600000, 3850600000, 2635300000, 1364500000, 80800000, 1565100000, 2201300000, 1599100000,
                    1946700000, 1788100000, 1082700000, 11098800000, 11377000000, 10022500000, 8496400000, 6511400000, 5587000000, 5769800000, 4716800000, 5063500000, 3739000000, 3438700000,
                    2096900000, 2354700000, 1731800000, 1038000000, -264300000, -750100000, -447500000, -157800000, 272300000, 1517400000, 1512300000, 1710100000, 1212500000, -14294700000,
                    -13101300000, -12979400000, -12663000000, -12163200000, -11385000000, -9392300000, -8379800000, -6203000000, -1635900000, 429400000, -302300000, -27480100000, -24220900000,
                    -22157800000, -20248700000, -18108300000, -15721900000, -12205400000, -10284500000, -7598400000, -5342800000, -3097100000, -1222700000, -32297200000, -28843700000, -26547700000,
                    -24285000000, -21581700000, -19727000000, -15833400000, -13153100000, -9828300000, -6331400000, -3512200000, -973000000, -33110900000, -29546700000, -26555500000, -23120300000,
                    -21424500000, -18705000000, -14947400000, -12322400000, -9313500000, -6407600000, -3539000000, -1886900000, -45811300000, -41889100000, -38118900000, -34038500000, -30954800000,
                    -27736900000, -23765000000, -19797600000, -14118100000, -10356200000, -6429400000, -3047100000, -52079800000, -46628600000, -40949800000, -36039900000, -31544600000, -27495700000,
                    -22797900000, -18455900000, -14380400000, -10181100000, -5592100000, -2528300000, -49223600000, -45178700000, -40870700000, -36507300000, -32038000000, -28184500000, -23595600000,
                    -19165200000, -15071000000, -10892700000, -6093100000, -2529300000, -43819200000, -38618700000, -34286100000, -30360100000, -26635100000, -23304600000, -19465700000, -15793100000,
                    -12199100000, -8465900000, -5424100000, -2981900000, -26281300000, -22710800000, -19835500000, -16996000000, -14721300000, -12976600000, -10876900000, -8597600000, -6424800000,
                    -4377500000, -2872600000, -1360200000,
                }
            ),
            new Series
            (
                new Dictionary<string, object>()
                {
                    { "Description", "Domestic Trade, Wholesale Trade" },
                    { "Region", "se" },
                    { "StartDate", new DateTime(2000, 01, 01, 0, 0, 0) },
                    { "Frequency", "monthly" },
                    { "CalAdj", "ca" },
                    { "PrimName", "setrad2195" },
                },
                new object[]
                {
                    139339400000, 122301700000, 104821000000, 88632500000, 72138400000, 54068200000, 35329100000, 18879900000, 188848100000, 177674900000, 156903400000, 136973300000, 118474100000,
                    103487700000, 88747800000, 74297800000, 58215300000, 42986600000, 29336400000, 15848200000, 168644000000, 160453000000, 144431700000, 128103300000, 111922200000, 97781700000,
                    86655400000, 74164000000, 60492000000, 45577500000, 29767200000, 16367800000, 156939600000, 144658900000, 129372100000, 115541200000, 102080400000, 90591700000, 80197800000,
                    65923800000, 53925100000, 43420600000, 29183500000, 15152400000, 148294700000, 138014800000, 124205100000, 110805300000, 96626600000, 85165700000, 74776600000, 62717700000,
                    51239000000, 39050000000, 26920600000, 14785500000, 118784200000, 110879600000, 101045500000, 89800500000, 77871000000, 68206000000, 59846600000, 51005100000, 41608800000,
                    30933100000, 21678500000, 12382500000, 97873500000, 92418100000, 83480000000, 73996500000, 63206200000, 55272700000, 47841100000, 39082800000, 32276600000, 22524100000,
                    16320300000, 9397300000, 77996400000, 74777500000, 67790800000, 58196300000, 50153700000, 42544800000, 36773700000, 29853900000, 23853800000, 18574500000, 13194500000, 7979700000,
                    54923100000, 51280500000, 45464300000, 38969200000, 31831400000, 27233600000, 25037700000, 21818700000, 18214200000, 14829900000, 10607500000, 6010500000, 48053700000, 45976100000,
                    42533400000, 38628400000, 34119000000, 31160300000, 28206900000, 23868000000, 18472600000, 12383100000, 9623100000, 4779600000, 41901500000, 38242100000, 33746200000, 29098900000,
                    24667800000, 22345200000, 19719200000, 16964300000, 13958200000, 11236500000, 8258600000, 3856300000, -7837000000, -5132000000, -5213900000, -5372100000, -5188700000, -4730900000,
                    -2630200000, -1155300000, 1504000000, 1818800000, 2388000000, 1829000000, 3214400000, 4785600000, 3850600000, 2635300000, 1364500000, 80800000, 1565100000, 2201300000, 1599100000,
                    1946700000, 1788100000, 1082700000, 11098800000, 11377000000, 10022500000, 8496400000, 6511400000, 5587000000, 5769800000, 4716800000, 5063500000, 3739000000, 3438700000,
                    2096900000, 2354700000, 1731800000, 1038000000, -264300000, -750100000, -447500000, -157800000, 272300000, 1517400000, 1512300000, 1710100000, 1212500000, -14294700000,
                    -13101300000, -12979400000, -12663000000, -12163200000, -11385000000, -9392300000, -8379800000, -6203000000, -1635900000, 429400000, -302300000, -27480100000, -24220900000,
                    -22157800000, -20248700000, -18108300000, -15721900000, -12205400000, -10284500000, -7598400000, -5342800000, -3097100000, -1222700000, -32297200000, -28843700000, -26547700000,
                    -24285000000, -21581700000, -19727000000, -15833400000, -13153100000, -9828300000, -6331400000, -3512200000, -973000000, -33110900000, -29546700000, -26555500000, -23120300000,
                    -21424500000, -18705000000, -14947400000, -12322400000, -9313500000, -6407600000, -3539000000, -1886900000, -45811300000, -41889100000, -38118900000, -34038500000, -30954800000,
                    -27736900000, -23765000000, -19797600000, -14118100000, -10356200000, -6429400000, -3047100000, -52079800000, -46628600000, -40949800000, -36039900000, -31544600000, -27495700000,
                    -22797900000, -18455900000, -14380400000, -10181100000, -5592100000, -2528300000, -49223600000, -45178700000, -40870700000, -36507300000, -32038000000, -28184500000, -23595600000,
                    -19165200000, -15071000000, -10892700000, -6093100000, -2529300000, -43819200000, -38618700000, -34286100000, -30360100000, -26635100000, -23304600000, -19465700000, -15793100000,
                    -12199100000, -8465900000, -5424100000, -2981900000, -26281300000, -22710800000, -19835500000, -16996000000, -14721300000, -12976600000, -10876900000, -8597600000, -6424800000,
                    -4377500000, -2872600000, -1360200000,
                }
            ),
            new Series(
                new Dictionary<string, object>
                {
                    { "Description", "DateTime skip test" },
                    { "Region", "us" },
                    { "StartDate", new DateTime(2017, 01, 01, 0, 0, 0) },
                    { "PrimName", "dt" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    5, 4, 3, 4,
                    5, 4, 3, 4,
                    5, 4, 3, 4,
                },
                new[]
                {
                    new DateTime(2017, 01, 01), new DateTime(2017, 01, 02), new DateTime(2017, 01, 03),new DateTime(2017, 01, 04),
                    new DateTime(2017, 01, 06), new DateTime(2017, 01, 07), new DateTime(2017, 01, 08), new DateTime(2017, 01, 09),
                    new DateTime(2017, 01, 11), new DateTime(2017, 01, 12), new DateTime(2017, 01, 13), new DateTime(2017, 01, 14),
                }
            ),
            new Series
            (
                new Dictionary<string, object>()
                {
                    { "Description", "Domestic Trade, Wholesale Trade" },
                    { "Region", "se" },
                    { "StartDate", new DateTime(1996, 01, 01, 0, 0, 0) },
                    { "Frequency", "monthly" },
                    { "PrimName", "setrad2136" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    139339400000, 122301700000, 104821000000, 88632500000, 72138400000, 54068200000, 35329100000, 18879900000, 188848100000, 177674900000, 156903400000, 136973300000, 118474100000,
                    103487700000, 88747800000, 74297800000, 58215300000, 42986600000, 29336400000, 15848200000, 168644000000, 160453000000, 144431700000, 128103300000, 111922200000, 97781700000,
                    86655400000, 74164000000, 60492000000, 45577500000, 29767200000, 16367800000, 156939600000, 144658900000, 129372100000, 115541200000, 102080400000, 90591700000, 80197800000,
                    65923800000, 53925100000, 43420600000, 29183500000, 15152400000, 148294700000, 138014800000, 124205100000, 110805300000, 96626600000, 85165700000, 74776600000, 62717700000,
                    51239000000, 39050000000, 26920600000, 14785500000, 118784200000, 110879600000, 101045500000, 89800500000, 77871000000, 68206000000, 59846600000, 51005100000, 41608800000,
                    30933100000, 21678500000, 12382500000, 97873500000, 92418100000, 83480000000, 73996500000, 63206200000, 55272700000, 47841100000, 39082800000, 32276600000, 22524100000,
                    16320300000, 9397300000, 77996400000, 74777500000, 67790800000, 58196300000, 50153700000, 42544800000, 36773700000, 29853900000, 23853800000, 18574500000, 13194500000, 7979700000,
                    54923100000, 51280500000, 45464300000, 38969200000, 31831400000, 27233600000, 25037700000, 21818700000, 18214200000, 14829900000, 10607500000, 6010500000, 48053700000, 45976100000,
                    42533400000, 38628400000, 34119000000, 31160300000, 28206900000, 23868000000, 18472600000, 12383100000, 9623100000, 4779600000, 41901500000, 38242100000, 33746200000, 29098900000,
                    24667800000, 22345200000, 19719200000, 16964300000, 13958200000, 11236500000, 8258600000, 3856300000, -7837000000, -5132000000, -5213900000, -5372100000, -5188700000, -4730900000,
                    -2630200000, -1155300000, 1504000000, 1818800000, 2388000000, 1829000000, 3214400000, 4785600000, 3850600000, 2635300000, 1364500000, 80800000, 1565100000, 2201300000, 1599100000,
                    1946700000, 1788100000, 1082700000, 11098800000, 11377000000, 10022500000, 8496400000, 6511400000, 5587000000, 5769800000, 4716800000, 5063500000, 3739000000, 3438700000,
                    2096900000, 2354700000, 1731800000, 1038000000, -264300000, -750100000, -447500000, -157800000, 272300000, 1517400000, 1512300000, 1710100000, 1212500000, -14294700000,
                    -13101300000, -12979400000, -12663000000, -12163200000, -11385000000, -9392300000, -8379800000, -6203000000, -1635900000, 429400000, -302300000, -27480100000, -24220900000,
                    -22157800000, -20248700000, -18108300000, -15721900000, -12205400000, -10284500000, -7598400000, -5342800000, -3097100000, -1222700000, -32297200000, -28843700000, -26547700000,
                    -24285000000, -21581700000, -19727000000, -15833400000, -13153100000, -9828300000, -6331400000, -3512200000, -973000000, -33110900000, -29546700000, -26555500000, -23120300000,
                    -21424500000, -18705000000, -14947400000, -12322400000, -9313500000, -6407600000, -3539000000, -1886900000, -45811300000, -41889100000, -38118900000, -34038500000, -30954800000,
                    -27736900000, -23765000000, -19797600000, -14118100000, -10356200000, -6429400000, -3047100000, -52079800000, -46628600000, -40949800000, -36039900000, -31544600000, -27495700000,
                    -22797900000, -18455900000, -14380400000, -10181100000, -5592100000, -2528300000, -49223600000, -45178700000, -40870700000, -36507300000, -32038000000, -28184500000, -23595600000,
                    -19165200000, -15071000000, -10892700000, -6093100000, -2529300000, -43819200000, -38618700000, -34286100000, -30360100000, -26635100000, -23304600000, -19465700000, -15793100000,
                    -12199100000, -8465900000, -5424100000, -2981900000, -26281300000, -22710800000, -19835500000, -16996000000, -14721300000, -12976600000, -10876900000, -8597600000, -6424800000,
                    -4377500000, -2872600000, -1360200000,
                }
            ),
            new Series
            (
                new Dictionary<string, object>
                {
                    { "Description", "Domestic Trade except of Motor Vehicles" },
                    { "Region", "se" },
                    { "StartDate", new DateTime(1996, 01, 01, 0, 0, 0) },
                    { "Frequency", "monthly" },
                    { "PrimName", "setrad2128" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    139339400000, 122301700000, 104821000000, 88632500000, 72138400000, 54068200000, 35329100000, 18879900000, 188848100000, 177674900000, 156903400000, 136973300000, 118474100000,
                    103487700000, 88747800000, 74297800000, 58215300000, 42986600000, 29336400000, 15848200000, 168644000000, 160453000000, 144431700000, 128103300000, 111922200000, 97781700000,
                    86655400000, 74164000000, 60492000000, 45577500000, 29767200000, 16367800000, 156939600000, 144658900000, 129372100000, 115541200000, 102080400000, 90591700000, 80197800000,
                    65923800000, 53925100000, 43420600000, 29183500000, 15152400000, 148294700000, 138014800000, 124205100000, 110805300000, 96626600000, 85165700000, 74776600000, 62717700000,
                    51239000000, 39050000000, 26920600000, 14785500000, 118784200000, 110879600000, 101045500000, 89800500000, 77871000000, 68206000000, 59846600000, 51005100000, 41608800000,
                    30933100000, 21678500000, 12382500000, 97873500000, 92418100000, 83480000000, 73996500000, 63206200000, 55272700000, 47841100000, 39082800000, 32276600000, 22524100000,
                    16320300000, 9397300000, 77996400000, 74777500000, 67790800000, 58196300000, 50153700000, 42544800000, 36773700000, 29853900000, 23853800000, 18574500000, 13194500000, 7979700000,
                    54923100000, 51280500000, 45464300000, 38969200000, 31831400000, 27233600000, 25037700000, 21818700000, 18214200000, 14829900000, 10607500000, 6010500000, 48053700000, 45976100000,
                    42533400000, 38628400000, 34119000000, 31160300000, 28206900000, 23868000000, 18472600000, 12383100000, 9623100000, 4779600000, 41901500000, 38242100000, 33746200000, 29098900000,
                    24667800000, 22345200000, 19719200000, 16964300000, 13958200000, 11236500000, 8258600000, 3856300000, -7837000000, -5132000000, -5213900000, -5372100000, -5188700000, -4730900000,
                    -2630200000, -1155300000, 1504000000, 1818800000, 2388000000, 1829000000, 3214400000, 4785600000, 3850600000, 2635300000, 1364500000, 80800000, 1565100000, 2201300000, 1599100000,
                    1946700000, 1788100000, 1082700000, 11098800000, 11377000000, 10022500000, 8496400000, 6511400000, 5587000000, 5769800000, 4716800000, 5063500000, 3739000000, 3438700000,
                    2096900000, 2354700000, 1731800000, 1038000000, -264300000, -750100000, -447500000, -157800000, 272300000, 1517400000, 1512300000, 1710100000, 1212500000, -14294700000,
                    -13101300000, -12979400000, -12663000000, -12163200000, -11385000000, -9392300000, -8379800000, -6203000000, -1635900000, 429400000, -302300000, -27480100000, -24220900000,
                    -22157800000, -20248700000, -18108300000, -15721900000, -12205400000, -10284500000, -7598400000, -5342800000, -3097100000, -1222700000, -32297200000, -28843700000, -26547700000,
                    -24285000000, -21581700000, -19727000000, -15833400000, -13153100000, -9828300000, -6331400000, -3512200000, -973000000, -33110900000, -29546700000, -26555500000, -23120300000,
                    -21424500000, -18705000000, -14947400000, -12322400000, -9313500000, -6407600000, -3539000000, -1886900000, -45811300000, -41889100000, -38118900000, -34038500000, -30954800000,
                    -27736900000, -23765000000, -19797600000, -14118100000, -10356200000, -6429400000, -3047100000, -52079800000, -46628600000, -40949800000, -36039900000, -31544600000, -27495700000,
                    -22797900000, -18455900000, -14380400000, -10181100000, -5592100000, -2528300000, -49223600000, -45178700000, -40870700000, -36507300000, -32038000000, -28184500000, -23595600000,
                    -19165200000, -15071000000, -10892700000, -6093100000, -2529300000, -43819200000, -38618700000, -34286100000, -30360100000, -26635100000, -23304600000, -19465700000, -15793100000,
                    -12199100000, -8465900000, -5424100000, -2981900000, -26281300000, -22710800000, -19835500000, -16996000000, -14721300000, -12976600000, -10876900000, -8597600000, -6424800000,
                    -4377500000, -2872600000, -1360200000,
                }
            ),
            new Series
            (
                new Dictionary<string, object>
                {
                    { "Description", "New Passenger Car" },
                    { "Region", "se" },
                    { "StartDate", new DateTime(1990, 01, 01, 0, 0, 0) },
                    { "Frequency", "monthly" },
                    { "PrimName", "ecb_stsmsewcregpc00003abs" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    27088, 29598, 23061, 32620, 31146, 30099, 30943, 23264, 19719, 23934, 25343, 22506, 19969, 23999, 12581, 67887, 36775, 34041, 38051, 27350, 22128, 35245, 31893, 32309, 32062,
                    29102, 25674, 38051, 34471, 32302, 36851, 27877, 23140, 35281, 31222, 33066, 31672, 26573, 25129, 36048, 33641, 32368, 35500, 26165, 22922, 32762, 31771, 31757, 29083, 27855,
                    23531, 31030, 30623, 30381, 31301, 23856, 20936, 27367, 27107, 27540, 25640, 25157, 19957, 29462, 27850, 27403, 28232, 21635, 18559, 27496, 25110, 24694, 24689, 21704, 17703,
                    24190, 25158, 23303, 23744, 17951, 15138, 29116, 24596, 23422, 22921, 20492, 18187, 26196, 25136, 23964, 28294, 20192, 18572, 24039, 26404, 26016, 26094, 23705, 20376, 28167,
                    29168, 29718, 28345, 21100, 19741, 28173, 26265, 27429, 25414, 22468, 20500, 27194, 25173, 25537, 24626, 18064, 16436, 18919, 20395, 21917, 18874, 16107, 14184, 21541, 18936,
                    18156, 17870, 14678, 11591, 17297, 18425, 21737, 22339, 18366, 16476, 24553, 26439, 25661, 25648, 20693, 16402, 33028, 27201, 28112, 24874, 22435, 20715, 27118, 29289, 27900,
                    27226, 20430, 19835, 24075, 24364, 24400, 24774, 21636, 19622, 28118, 28129, 27355, 24537, 18663, 17023, 23660, 24169, 25852, 24594, 19807, 17848, 26266, 24822, 24790, 24480,
                    19171, 16975, 24059, 23245, 23524, 22029, 17910, 16806, 26145, 24882, 25364, 24179, 17771, 16433, 21156, 21189, 24262, 21459, 18225, 17710, 25169, 25790, 23544, 25762, 19360,
                    19045, 21839, 22657, 22663, 22373, 18694, 16971, 23428, 24794, 23434, 24211, 18944, 15689, 22553, 20857, 20849, 19777, 17920, 14948, 24459, 24474, 23587, 23416, 18018, 15980,
                    28751, 23028, 23440, 24638, 21389, 20122, 26677, 30587, 27075, 27977, 19635, 16746, 31921, 27339, 27377, 23381, 19275, 20547, 25201, 27276, 24643, 26537, 20417, 18901, 22907,
                    23031, 24285, 20141, 17818, 17070, 23537, 22776, 23856, 21683, 18726, 18227, 19845, 20938, 21892, 18018, 15798, 14663, 20735, 19590, 21786, 21856, 17420, 13914, 18308, 19403,
                    19458, 14971, 10938, 11882, 17423, 16731, 15839, 14509, 14281, 10460, 15224, 14908, 16799, 14788, 9959, 8494, 15531, 15236, 16200, 16355, 12889, 13269, 14175, 14165, 14167, 12316,
                    8597, 8199, 15051, 15564, 16174, 15435, 10769, 10666, 11790, 12178, 12664, 10314, 7819, 6488, 9730, 11330, 10863, 12600, 9598, 8491, 11514, 10069, 11438, 10580, 8688, 10111, 14749,
                    16395, 17028, 16715, 13704, 13214, 16394, 17448, 17939, 15014, 10713, 11057, 17822, 19223, 19859, 18386, 13468, 11742, 15236, 16191, 19397, 16928, 13461, 12858, 23899, 22922,
                    22621, 25219, 20986, 20586,
                }
            ),
            new Series
            (
                new Dictionary<string, object>
                {
                    { "Description", "Export (Goods)" },
                    { "Region", "se" },
                    { "StartDate", new DateTime(1995, 01, 01, 0, 0, 0) },
                    { "Frequency", "quarterly" },
                    { "PrimName", "setrad1588" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    116, 116, 119, 105, 114, 112, 115, 102, 108, 106, 108, 97, 105, 101, 105, 96, 102, 97, 99, 93, 98, 95, 97, 91, 98, 95, 98, 94, 101, 100, 100, 98, 101, 100, 102, 89, 94, 84, 84, 76,
                    80, 79, 91, 93, 104, 101, 101, 89, 96, 95, 99, 86, 93, 93, 91, 83, 90, 82, 86, 77, 86, 80, 79, 71, 76, 74, 74, 65, 74, 71, 72, 64, 69, 71, 76, 67, 71, 67, 68, 60, 63, 60, 64, 55,
                    58, 58, 58, 51, 56, 51, 53, 45, 50, 48, 49, 42, 46, 47,
                }
            ),
            new Series
            (
                new Dictionary<string, object>
                {
                    { "Description", "Hotel Nights, United States" },
                    { "Region", "se" },
                    { "StartDate", new DateTime(1978, 01, 01, 0, 0, 0) },
                    { "Frequency", "monthly" },
                    { "PrimName", "setour0039" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    119199, 159262, 123550, 87371, 58000, 55460, 43790, 42718, 51002, 53597, 58577, 75251, 111408, 127250, 104976, 68535, 46600, 46609, 35013, 34290, 43097, 43563, 47700, 70409,
                    101419, 121713, 101422, 68253, 42424, 39371, 31770, 29747, 28193, 22457, 33038, 49698, 68151, 80778, 75839, 50382, 29826, 27733, 21733, 20479, 23959, 28876, 35348, 48670, 76962,
                    71707, 71983, 42687, 25958, 26064, 20415, 19487, 19533, 22301, 29164, 45614, 62966, 70563, 67720, 47052, 26881, 28933, 21562, 19928, 22266, 20987, 26639, 41325, 62166, 62837,
                    64685, 37741, 24395, 22139, 17481, 18357, 18390, 21206, 25606, 40666, 64964, 78055, 62311, 35436, 22692, 19777, 17228, 17866, 18514, 21255, 28221, 38576, 56551, 67195, 59248,
                    35172, 22310, 19913, 15941, 16186, 17634, 19681, 30703, 37978, 60789, 68258, 48713, 36664, 17946, 18873, 16253, 14302, 18084, 18689, 22766, 30477, 46579, 53046, 44865, 30568,
                    19654, 18624, 15359, 15326, 16265, 16979, 26112, 37131, 47198, 54770, 53670, 32715, 25395, 25579, 17053, 17214, 20418, 22164, 25805, 43529, 60177, 68079, 60884, 36059, 23467,
                    22222, 17428, 16962, 19140, 18687, 24387, 39558, 59971, 65293, 61458, 36455, 23422, 20802, 17013, 17619, 20984, 19339, 22138, 41995, 58655, 64130, 62840, 38572, 23702, 20288,
                    15534, 14731, 16668, 17571, 24562, 36121, 52379, 68235, 53411, 30299, 19067, 20535, 13598, 12912, 16585, 18700, 20712, 35140, 51594, 55764, 56110, 30806, 14993, 19532, 13670,
                    15492, 15706, 16995, 22510, 34271, 53693, 71091, 61225, 33236, 20301, 17389, 15010, 15404, 15560, 15241, 20569, 36744, 62534, 79545, 69426, 38043, 20084, 19899, 16257, 16397,
                    17319, 18043, 25614, 41728, 54290, 63979, 56860, 32592, 17526, 22245, 14892, 16225, 14216, 16123, 21876, 33886, 60133, 66088, 58759, 31219, 18515, 20331, 13443, 13344, 12035,
                    14680, 20417, 33476, 54323, 61841, 58296, 29043, 15162, 15517, 11275, 11968, 11448, 14646, 20299, 32795, 55807, 55754, 56615, 29435, 16426, 14491, 12477, 13392, 10338, 13588,
                    19625, 30241, 49857, 51754, 52555, 28718, 13912, 16743, 11455, 12655, 11510, 16007, 21791, 29722, 49781, 50367, 53502, 29625, 13990, 15838, 12215, 10700, 10349, 14139, 19134,
                    34237, 52406, 57071, 54776, 28657, 15337, 12615, 10775, 10654, 10171, 13750, 17138, 32379, 47217, 43974, 48210, 25695, 13227, 14416, 9812, 10298, 8952, 10402, 14620, 27741, 47271,
                    47173, 41716, 24235, 12118, 14151, 10300, 8649, 9533, 12028, 16835, 27432, 40571, 37702, 38802, 21287, 12617, 9064, 6211, 6413, 8157, 11697, 18941, 33996, 58732, 60596, 55027,
                    31179, 14632, 13959, 9820, 10164, 11394, 12978, 19627, 40324, 58125, 54006, 57670, 30607, 16570, 12299, 8612, 9156, 8831, 11243, 17859, 35200, 64623, 55278, 64831, 29668, 12021,
                    10987, 7447, 7533, 7320, 10494, 16259, 41128, 62642, 69754, 61532, 33070, 12687, 12597, 10126, 10984, 10340, 14161, 16017, 40050, 56974, 56678, 58644, 30283, 14041, 11846, 8209,
                    8427, 8587, 11813, 24685, 56496, 101993, 106408, 91188, 33968, 14081, 13006, 10019, 10313, 10455, 12285, 21545, 47495, 83639, 84887, 73424, 33874, 15197, 12567, 10522, 10932, 9628,
                    12448, 19089, 44843, 87106, 86282, 62752, 31624, 14624, 11554, 7756, 7632, 7542, 9038, 14525, 29274, 59919, 62863, 49547, 24634, 12508, 10322, 7657, 6356, 6870, 9259, 14622, 27547,
                    52750, 54295, 46492, 27691, 10806, 9337, 6676, 6580, 5536, 8691, 15053, 26833, 48905, 50987, 43949, 15246, 9511, 8758, 7983, 7880, 6663, 10117, 15739, 30687, 52933, 66973, 49536,
                    24886, 10089, 9026, 5710, 7420, 6228, 9199, 13843, 33920, 54512, 61494, 51200, 25524, 11072, 7867, 6706, 7160,
                }
            ),
            new Series
            (
                new Dictionary<string, object>
                {
                    { "Description", "Youth Hotel Nights, United States" },
                    { "Region", "se" },
                    { "StartDate", new DateTime(1978, 01, 01, 0, 0, 0) },
                    { "Frequency", "monthly" },
                    { "PrimName", "setour0141" },
                    { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 0, 0, 0) },
                },
                new object[]
                {
                    3799, 4514, 6171, 4194, 2539, 2079, 1793, 2006, 1626, 1992, 2039, 3363, 5157, 5539, 5813, 4878, 2045, 2894, 2023, 3383, 2536, 2085, 2483, 3823, 4556, 6285, 5707, 4861, 2661, 2672,
                    2652, 2787, 1896, 1585, 2081, 3655, 5643, 4343, 5185, 3276, 1752, 1818, 1661, 1429, 1395, 1823, 2797, 3081, 3678, 4896, 5889, 4290, 2296, 1880, 1658, 1827, 1268, 1458, 2236, 2754,
                    4113, 4741, 4687, 3144, 1896, 1421, 1157, 1154, 1091, 1495, 1421, 2497, 3538, 3986, 4293, 2673, 1353, 1847, 903, 1185, 1082, 1127, 1459, 2584, 3741, 3725, 3451, 2290, 1056, 1176,
                    735, 986, 595, 757, 1162, 1672, 2691, 3301, 2738, 2113, 1829, 1223, 893, 1169, 518, 681, 1050, 1486, 2391, 3249, 2873, 1856, 950, 1119, 952, 791, 864, 922, 991, 1501, 2680, 3062,
                    2850, 1534, 1100, 1350, 786, 947, 819, 910, 824, 1546, 2598, 4028, 4260, 1991, 1678, 1474, 1211, 720, 964, 1084, 902, 1701, 2397, 2992, 3153, 1481, 1165, 1484, 768, 773, 738, 749,
                    948, 1348, 2277, 2981, 2417, 1719, 879, 759, 591, 511, 573, 759, 859, 1281, 2075, 2492, 2351, 1600, 827, 1068, 893, 810, 546, 806, 1094, 1339, 2480, 2722, 2904, 1336, 900, 742,
                    604, 623, 458, 686, 632, 1119, 1937, 2283, 2372, 1344, 620, 767, 393, 316, 430, 474, 730, 979, 1982, 2786, 2564, 961, 649, 866, 612, 381, 160, 385, 413, 1134, 2015, 2313, 2179,
                    770, 438, 521, 309, 154, 250, 346, 492, 981, 1749, 2727, 1887, 682, 377, 486, 160, 241, 200, 302, 564, 870, 1593, 2230, 1975, 879, 478, 694, 290, 154, 168, 296, 535, 948, 1369,
                    1719, 1551, 720, 367, 274, 248, 144, 293, 253, 488, 608, 1259, 1975, 1587, 566, 301, 275, 244, 188, 257, 268, 408, 794, 1649, 1842, 2543, 666, 404, 270, 119, 122, 207, 219, 578,
                    771, 2052, 2565, 2412, 634, 465, 300, 167, 96, 267, 411, 488, 877, 1460, 2343, 2056, 685, 496, 343, 247, 240, 248, 279, 491, 1085, 1448, 2327, 2138, 777, 763, 273, 242, 110, 187,
                    248, 639, 1080, 1988, 2420, 3010, 844, 623, 432, 282, 159, 165, 266, 673, 968, 1407, 1862, 2212, 1072, 516, 513, 427, 250, 282, 332, 1058, 1252, 2269, 3162, 3219, 1095, 446, 312,
                    282, 150, 295, 338, 794, 1232, 2061, 3553, 3555, 1265, 525, 429, 149, 137, 297, 253, 734, 1404, 2046, 2875, 2921, 844, 386, 209, 253, 260, 273, 393, 874, 1514, 1932, 3569, 3541,
                    1024, 543, 313, 122, 261, 607, 414, 1001, 1404, 2694, 2544, 2538, 967, 426, 365, 388, 213, 452, 427, 1094, 1255, 2576, 3112, 2984, 828, 417, 482, 252, 115, 236, 246, 1001, 1639,
                    2552, 2911, 2737, 763, 402, 214, 206, 141, 213, 443, 899, 1069, 2598, 2731, 2237, 502, 457, 214, 405, 140, 124, 305, 681, 1019, 3300, 3044, 2607, 645, 400, 304, 96, 93, 150, 323,
                    821, 1003, 2499, 2621, 3128, 471, 421, 185, 147, 122, 164, 368, 906, 950, 2013, 3552, 2496, 392, 252, 142, 24, 18, 24, 85, 990, 819, 3147, 3945, 3100, 219, 360, 69, 15, 4, 19, 45,
                    665, 777, 1106, 1637, 786, 386, 289, 144, 17, 9,
                }
            ),
        };

        /// <summary>
        /// The search database, which groups SeriesRow in Groups and Aspects
        /// </summary>
        private static readonly Dictionary<string, List<object>> BrowseDataBase = new Dictionary<string, List<object>>()
        {
            {
                string.Empty,
                new List<object>
                {
                    new Dictionary<string, object>
                    {
                        { "Description", "Sweden" },
                        {
                            "Children",
                            new List<Dictionary<string, object>>()
                            {
                                new Dictionary<string, object> { { "Description", "Trade" }, { "ChildrenReference", "SwedenTrade" } },
                                new Dictionary<string, object> { { "Description", "Tourism" }, { "ChildrenReference", "SwedenTourism" } }
                            }
                        }
                    },
                    new Dictionary<string, object> {
                        { "Description", "Poland" },
                        {
                            "Children",
                            new List<Dictionary<string, object>>
                            {
                                new Dictionary<string, object> {
                                    { "Description", "Tourism" },
                                    {
                                        "Children",
                                        new List<Dictionary<string, object>> { new Dictionary<string, object> {{ "Description", "Arrivals" }, { "SeriesReference", "PolandTourismArrivals" }} }
                                    }
                                },
                                new Dictionary<string, object>
                                {
                                    { "Description", "Trade" },
                                    { "Children",
                                        new List<Dictionary<string, object>>
                                        {
                                            new Dictionary<string, object> {
                                                {"Description", "Domestic Trade"},
                                                {
                                                    "Children",
                                                    new List<Dictionary<string, object>>
                                                    {
                                                        new Dictionary<string, object> {{"Description", "Wholesale Trade"}, {"SeriesReference", "PolandTradeDomesticTradeWholesaleTrade"}},
                                                        new Dictionary<string, object> {{"Description", "ECB Passenger Car Registration"}, {"SeriesReference", "PolandTradeDomesticEBCCarRegistration"}}
                                                    }
                                                }
                                            },
                                            new Dictionary<string, object>
                                            {
                                                { "Description", "Foreign Trade" },
                                                {
                                                    "Children",
                                                    new List<Dictionary<string, object>>
                                                    {
                                                        new Dictionary<string, object>
                                                        {
                                                            {"Description", "Countries"},
                                                            {
                                                                "Children",
                                                                new List<Dictionary<string, object>>
                                                                {
                                                                    new Dictionary<string, object> {{"Description", "Export"}, {"SeriesReference", "PolandTradeForeignCountriesExport"}},
                                                                    new Dictionary<string, object> {{"Description", "Import"}, {"SeriesReference", "PolandTradeForeignCountriesImport" } },
                                                                    new Dictionary<string, object> {{"Description", "Trade Balance"}, {"SeriesReference", "PolandTradeForeignCountriesBalance" } },
                                                                }
                                                            }
                                                        },
                                                    }
                                                }
                                            },
                                        }
                                    }
                                }
                            }
                        }
                    },
                    new Dictionary<string, object>()
                    {
                        { "Description", "Other" },
                        { "SeriesReference", "Other" }
                    }
                    ,
                    new Dictionary<string, object>()
                    {
                        { "Description", "With revisions" },
                        { "SeriesReference", "WithRevisions" }
                    }
                }
            },
            {
                "SwedenTrade",
                new List<object>
                {
                    new Dictionary<string, object> { { "Description", "Domestic Trade" }, { "ChildrenReference", "SwedenDomesticTrade" } },
                    new Dictionary<string, object> { { "Description", "Foreign Trade" }, { "ChildrenReference", "SwedenForeignTrade" } },
                }
            },
            {
                "SwedenTourism",
                new List<object>
                {
                    new Dictionary<string, object>
                    {
                        { "Description", "Nights" },
                        {
                            "Children",
                            new List<Dictionary<string, object>>
                            {
                                new Dictionary<string, object> {{ "Description", "Hotels" }, { "SeriesReference", "SwedenTourismHotels" } },
                                new Dictionary<string, object> {{ "Description", "Youth Hostels" }, { "SeriesReference", "SwedenTourismYouthHostels" } },
                            }
                        }
                    }
                }
            },
            {
                "SwedenDomesticTrade",
                new List<object>
                {
                    new Dictionary<string, object>
                    {
                        { "Description", "Wholesale Trade" },
                        {
                            "Children",
                            new List<Dictionary<string, object>>
                            {
                                new Dictionary<string, object> { { "Description", "Sector Totals" }, { "SeriesReference", "SwedenWholesaleTradeSectorTotals" } },
                                new Dictionary<string, object> { { "Description", "Totals" }, { "SeriesReference", "SwedenWholesaleTradeTotals" } },
                            }
                        }
                    },
                    new Dictionary<string, object> {{"Description", "ECB Passenger Car Registration"}, {"SeriesReference", "SwedenTradeECBCarRegistration"}}
                }
            },
            {
                "SwedenForeignTrade",
                new List<object>
                {
                    new Dictionary<string, object>
                    {
                        { "Description", "Totals" },
                        { "SeriesReference", "SwedenForeignTradeTotals" }
                    }
                }
            }
        };

        private static readonly Dictionary<string, SeriesList> SeriesMetaData = new Dictionary<string, SeriesList>
        {
            {
                "Other", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            Array.Empty<SeriesRow>()
                        )
                    }
                )
            },
            {
                "WithRevisions", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "withrev"
                                    }
                                ),
                            }
                        )
                    }
                )
            },
            {
                "PolandTourismArrivals", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "pltour0001"
                                    }
                                ),
                            }
                        )
                    }
                )
            },
            {
                "PolandTradeDomesticTradeWholesaleTrade", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            "Food",
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "pltrad0014",
                                    }
                                ),
                            }
                        ),
                        new Group
                        (
                            "Other",
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "pltrad0021",
                                    }
                                ),
                            }
                        ),
                    }
                )
            },
            {
                "PolandTradeDomesticEBCCarRegistration", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "ecb_stsmplwcregpc00003abs",
                                    }
                                ),
                            }
                        )
                    }
                )
            },
            {
                "PolandTradeForeignCountriesExport", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "pltrad0135",
                                    }
                                ),
                            }
                        )
                    }
                )
            },
            {
                "PolandTradeForeignCountriesImport", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "pltrad0131",
                                    }
                                ),
                            }
                        )
                    }
                )
            },
            {
                "PolandTradeForeignCountriesBalance", new SeriesList
                (
                    new[] { new Aspect("Aspect 1", "The first aspect"), new Aspect("Aspect 2", "The second aspect") },
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "pltrad0051",
                                    }
                                ),
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "pltrad0131",
                                    }
                                ),
                            }
                        )
                    }
                )
            },
            {
                "SwedenWholesaleTradeSectorTotals", new SeriesList
                (
                    new [] { new Aspect("Calendar Adjusted, Current Prices", "Calendar Adjusted, Current Prices"), new Aspect("Constant Prices", "Constant Prices"), },
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "setrad2195",
                                        "setrad2136",
                                    }
                                ),
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "setrad2136",
                                        "setrad2195",
                                    }
                                ),
                            }
                        )
                    }
                )
            },
            {
                "SwedenWholesaleTradeTotals", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "setrad2128",
                                        "setrad2136",
                                    }
                                ),
                            }
                        )
                    }
                )
            },
            {
                "SwedenTradeECBCarRegistration", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "ecb_stsmsewcregpc00003abs",
                                    }
                                ),
                            }
                        )
                    }
                )
            },
            {
                "SwedenForeignTradeTotals", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    true,
                                    false,
                                    new []
                                    {
                                        "setrad1588",
                                    }
                                ),
                            }
                        )
                    }
                )
            },
            {
                "SwedenTourismHotels", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "setour0039",
                                    }
                                ),
                            }
                        )
                    }
                )
            },
            {
                "SwedenTourismYouthHostels", new SeriesList
                (
                    null,
                    new[]
                    {
                        new Group
                        (
                            string.Empty,
                            new[]
                            {
                                new SeriesRow
                                (
                                    string.Empty,
                                    0,
                                    false,
                                    false,
                                    new []
                                    {
                                        "setour0141",
                                    }
                                ),
                            }
                        )
                    }
                )
            },
        };

        private static readonly Dictionary<string, object> WithRevMetaData = new()
        {
            { "Description", "Series with revisions" },
            { "Region", "pl" },
            { "StartDate", new DateTime(2017, 01, 01, 0, 0, 0) },
            { "Frequency", "annual" },
            { "PrimName", "withrev" },
            { "LastModifiedTimeStamp", new DateTime(2019, 01, 01, 12, 0, 0) },
            { "StoresRevisionHistory", "1" },
            { "FirstRevisionTimeStamp", new DateTime(2017, 01, 01, 0, 0, 0, DateTimeKind.Utc) },
            { "LastRevisionTimeStamp", new DateTime(2019, 01, 01, 12, 0, 0, DateTimeKind.Utc) },
        };

        private static readonly List<(DateTime Vintage, string? Label, Series Series)> WithRevSeriesVintages = new()
        {
            ( new DateTime(2017, 1 , 1, 0, 0, 0, DateTimeKind.Utc), "Initial", new Series
                (
                    new Dictionary<string, object>()
                    {
                    },
                    new object[]
                    {
                        5726277300
                    }
                )
            ),
            ( new DateTime(2018, 1 , 1, 0, 0, 0, DateTimeKind.Utc), null, new Series
                (
                    new Dictionary<string, object>()
                    {
                    },
                    new object[]
                    {
                        5726277300, 6568862600
                    }
                )
            ),
            ( new DateTime(2019, 1 , 1, 0, 0, 0, DateTimeKind.Utc), null, new Series
                (
                    new Dictionary<string, object>()
                    {
                        { "StartDate", new DateTime(2014, 01, 01, 0, 0, 0) },
                    },
                    new object[]
                    {
                        5726277300, 6568862600, 5374127000, 5374127000
                    }, 
                    new DateTime[]
                    { 
                        new DateTime(2014, 1, 1), new DateTime(2017, 1, 1), new DateTime(2018, 1, 1), new DateTime(2019, 1, 1)
                    }
                )
            ),
            ( new DateTime(2019, 1 , 1, 12, 0, 0, DateTimeKind.Utc), "Update", new Series
                (
                    new Dictionary<string, object>()
                    {
                    },
                    new object[]
                    {
                        5726277300, 6568862600, 5974127000
                    },
                    PerValueMetaData: new Dictionary<string, object>?[]
                    {
                        new() { { "RevisionTimeStamp", new DateTime(2017, 01, 01, 0, 0, 0, DateTimeKind.Utc) } },
                        new() { { "RevisionTimeStamp", new DateTime(2018, 1 , 1, 0, 0, 0, DateTimeKind.Utc) } },
                        new() { { "RevisionTimeStamp", new DateTime(2019, 1, 1, 12, 0, 0, DateTimeKind.Utc) } },
                    }
                )
            ),
        };

        private static readonly List<Series> WithRevSeriesReleases = new()
        {
            new Series
                (
                    new Dictionary<string, object>()
                    {
                    },
                    new object[]
                    {
                        5726277300, 6568862600, 5374127000
                    },
                    PerValueMetaData: new Dictionary<string, object>?[]
                    {
                        new() { { "RevisionTimeStamp", new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc) } },
                        new() { { "RevisionTimeStamp", new DateTime(2018, 1 , 1, 0, 0, 0, DateTimeKind.Utc) } },
                        new() { { "RevisionTimeStamp", new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc) } },
                    }
                ),
            new Series
                (
                    new Dictionary<string, object>()
                    {
                    },
                    new object[]
                    {
                        double.NaN, double.NaN, 5974127000
                    },
                    PerValueMetaData: new Dictionary<string, object>?[]
                    {
                        null,
                        null,
                        new() { { "RevisionTimeStamp", new DateTime(2019, 1, 1, 12, 0, 0, DateTimeKind.Utc) } },
                    }
                ),
                new Series
                (
                    new Dictionary<string, object>()
                    {
                    },
                    new object[]
                    {
                        double.NaN, double.NaN, double.NaN
                    }
                ),
        };
    }
}

