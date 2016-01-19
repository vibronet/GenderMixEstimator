using Newtonsoft.Json.Linq;
using PhoneNumbers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace GenderCrawler
{
    public class Account
    {
        // personal info
        string FirstName;
        string UPN;
        string Country;
        GenderizeResult GenderData;
        
        // org info
        List<Account> Reports;
        public int Males;
        public int Females;
        public int Contacts;
        public int Undefined;

        // infers the user's ISO country code from the user's phone number
        // if there's no phone number, or it lacks a country code, the method defaults to US
        public static string InferCountryFromPhoneNumber(string phoneNumber)
        {
            try
            { 
                PhoneNumberUtil pnu = PhoneNumberUtil.GetInstance();
                PhoneNumber pn = pnu.Parse(phoneNumber, "");
                return pnu.GetRegionCodeForCountryCode(pn.CountryCode);
            }
            catch
            {
                // fits my data set. sorry! :)
                // also, usually it's US people who omit the country code...
                return "US";
            }
        }
        // default constructor
        public Account()
        {}

        // builds an account (but not its org yet)
        // important - in tis current form, anything taking more than 1 hour to execute will not work
        // solution: move the AuthenticationContext and token acquisition logic here from Main. But I am lazy.
        public static async Task<Account> CreateAsync(string upn, string accessToken)
        {
            Account newAccount = new Account();
            newAccount.UPN = upn;
            string[] upnComponents = upn.Split('@');
            string alias = upnComponents[0];
            string domain = upnComponents[1];
            string nameQuery = String.Format("https://graph.windows.net/{0}/users/{1}/givenName?api-version=1.6", domain, upn);
            string phoneQuery = String.Format("https://graph.windows.net/{0}/users/{1}/telephoneNumber?api-version=1.6", domain, upn);
            
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            //get the first name of the user
            HttpResponseMessage response = await httpClient.GetAsync(nameQuery);
            if (response.IsSuccessStatusCode)
            {
                string rez = await response.Content.ReadAsStringAsync();
                JObject jo = JObject.Parse(rez);
                newAccount.FirstName = (string)jo["value"];
            }

            // get the phone number of the user and infer its country of residence from it
            response = await httpClient.GetAsync(phoneQuery);
            if (response.IsSuccessStatusCode)
            {
                string rez = await response.Content.ReadAsStringAsync();
                JObject jo = JObject.Parse(rez);
                string phoneNum = (string)jo["value"];

                newAccount.Country = InferCountryFromPhoneNumber(phoneNum);
            }
            newAccount.Reports = new List<Account>();

            return newAccount;
        }

     
        // infers the gender of the current user, then recursely infers the gender mix for all of his/her reports
        // populated the Reports collection in the process   
        // same note about 1 hour execution limit applies here
        public async void GetGenderMix(string accessToken)
        {
            Males = Females = Undefined = Contacts = 0;

            // infer account's gender
            GenderData = await GenderizeProxy.GenderizeName(FirstName, Country);

            // if we could not establish the user's gender
            if (GenderData == null)
            {
                Undefined = 1;
            }
            else
            {
                // we have a successful inference. The account is the first entry for his/her own org            
                if (GenderData.Gender == "male")
                {
                    Males = 1;
                }
                else
                {
                    Females = 1;
                }
            }
            // retrieve all the direct reports of the current account
            string[] upnComponents = UPN.Split('@');
            string alias = upnComponents[0];
            string domain = upnComponents[1];
            string reportsQuery = String.Format("https://graph.windows.net/{0}/users/{1}/directReports?api-version=1.6", domain, UPN);
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            HttpResponseMessage response = httpClient.GetAsync(reportsQuery).Result;
            if (response.IsSuccessStatusCode)
            {
                string rez = response.Content.ReadAsStringAsync().Result;
                JObject jo = JObject.Parse(rez);

                // if the account does have reports
                foreach (JObject jreport in jo["value"])
                {
                    if ((string)jreport["userPrincipalName"] != null)
                    {
                        // create an account representing the report
                        Account report = new Account
                        {
                            UPN = (string)jreport["userPrincipalName"],
                            FirstName = (string)jreport["givenName"],
                            Country = InferCountryFromPhoneNumber((string)jreport["telephoneNumber"]),
                            Reports = new List<Account>()
                        };
                        // recurse on it
                        report.GetGenderMix(accessToken);
                        // fold back into the tally the gender numbers of the sub org
                        Males += report.Males;
                        Females += report.Females;
                        Undefined += report.Undefined;
                        Contacts += report.Contacts;
                        // append the Account represeting the report to the Reports collection
                        Reports.Add(report);
                    }
                    // no UPN means that the accunt is not a User, but a Contact. We don't count those here.
                    else
                        Contacts += 1;
                }
            }
            if (Reports.Count != 0)
                Console.WriteLine(" {0}'s org - {1} males, {2} females, {3} indetermined", UPN, Males, Females, Undefined);

        }
        // dumps the org data in CSV format
        public string PrintOrganization()
        {            
            string body = PrintAccount(String.Empty);
            string rez = "UPN, First Name, Gender, Males in org, Females in org, Undefined entries, Contacts, Males% in org, Females% in org"+Environment.NewLine+body;
            return rez;
        }

        string PrintAccount(string prefix)
        {
            string rez = string.Empty;
            if (Reports.Count != 0)
            {
                rez += prefix + string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}" + Environment.NewLine,
                    UPN,
                    FirstName,
                    (GenderData!= null)? GenderData.Gender : "undefined",
                    Males,
                    Females,
                    Undefined,
                    Contacts,
                    (float)Males / (Males + Females),
                    (float)Females / (Males + Females)
                    );
                foreach (Account a in Reports)
                {
                    rez += a.PrintAccount(prefix + ".");
                }
            }
            return rez;
        }
    }    
}
