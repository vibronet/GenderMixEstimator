using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization.Formatters;
using System.Text;
using System.Threading.Tasks;

namespace GenderCrawler
{
    // class used to hold the outcome of one call to the Genderize API 
    public class GenderizeResult
    {
        public string Name, Gender;
        public float Probability;
        public int Count;
        public GenderizeResult()
        { }       
    }
    // class used to interact with the Genderize API (https://genderize.io/)
    static class GenderizeProxy
    {
        static Dictionary<string, GenderizeResult> Cache = new Dictionary<string, GenderizeResult>();        
        static string ApiKey = String.Empty;
        static string cachePath = "NameCache.JSON";
        static float ProbabilityThreshold = 0.50F;
        static int CountThreshold = 1;

        public static async Task<GenderizeResult> GenderizeName(string name, string country)
        {            
            // if there's a cached entry for this first name and country, return it
            if (Cache.ContainsKey(name + "_" + country))
            {
                if (Cache[name + "_" + country] == null)
                    return null;
                // well, return it only if it meets the confidence and count requirements
                if((Cache[name + "_" + country].Probability>=ProbabilityThreshold)&&(Cache[name + "_" + country].Count>=CountThreshold))
                    return Cache[name + "_" + country];
            }
            else
            {
                HttpClient client = new HttpClient();

                // query the API to infer gender from the first name AND the country of the user
                string query = string.Format("https://api.genderize.io/?name={0}&country_id={1}{2}", name, country, (ApiKey != String.Empty) ? "&apikey=" + ApiKey : string.Empty);
                var response =  client.GetAsync(query).Result;
                if (response.IsSuccessStatusCode)
                {
                    JObject jo = JObject.Parse(await response.Content.ReadAsStringAsync());
                    // if the gender proerty comes back as null, it's possible that there are no entries for that name in the specified country
                    // let's try to infer the gender from the first name alone, then
                    if ((string)jo["gender"] == null)
                    {
                        query = string.Format("https://api.genderize.io/?name={0}{1}", name, (ApiKey != String.Empty) ? "&apikey=" + ApiKey : string.Empty);
                        response = client.GetAsync(query).Result;
                        jo = JObject.Parse(await response.Content.ReadAsStringAsync());
                    }
                    // if we did get a gender inference
                    if ((string)jo["gender"] != null)
                    {
                        GenderizeResult gr = new GenderizeResult
                        {
                            Name = (string)jo["name"],
                            Gender = (string)jo["gender"],
                            Probability = (float)jo["probability"],
                            Count = (int)jo["count"]
                        };
                        // cache the result
                        Cache.Add(name + "_" + country, gr);
                        // if the confidence meets the thresholds, return the GenderizeResult
                        if ((gr.Probability >= ProbabilityThreshold) && (gr.Count >= CountThreshold))
                            return gr;
                    }
                    else
                    {
                        // the name was not present at all in the API.
                        // add placeholder in cache, so we don't keep calling the API needlessly for this name/country combination
                        Cache.Add(name + "_" + country, null);
                    }
                }
            }
            return null;
        }

        public static void SerializeCache()
        {
            string json = JsonConvert.SerializeObject(Cache, Formatting.Indented, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                TypeNameAssemblyFormat = FormatterAssemblyStyle.Simple
            });
            File.WriteAllText(cachePath, json);
        }
        public static void DeserializeCache()
        {
            if (!File.Exists(cachePath))
                return;

            string json = File.ReadAllText(cachePath);
            Cache = (Dictionary<string, GenderizeResult>) JsonConvert.DeserializeObject(json, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                TypeNameAssemblyFormat = FormatterAssemblyStyle.Simple
            });

        }
    }
}
