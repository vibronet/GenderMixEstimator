using Microsoft.IdentityModel.Clients.ActiveDirectory;
using PhoneNumbers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenderCrawler
{
    class Program
    {
        static void Main(string[] args)
        {
            string commandString = string.Empty;
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("**************************************************");
            Console.WriteLine("*              GenderMix Text Client             *");
            Console.WriteLine("*                                                *");
            Console.WriteLine("*     How diverse is your org? FInd out here!    *");
            Console.WriteLine("*                                                *");
            Console.WriteLine("**************************************************");
            Console.WriteLine("");
            Console.ResetColor();
                      
            // gather alias and domain
            Console.WriteLine("Enter the UPN of the manager whose org you want to evaluate:");
            Console.WriteLine("(format: ALIAS@DOMAIN)");
            commandString = Console.ReadLine();

            string[] upnComponents = commandString.Split('@');
            if (upnComponents.Count() != 2)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Invalid UPN format");
                Console.ResetColor();
                commandString = Console.ReadLine();
                return;
            }
            string alias = upnComponents[0];
            string domain = upnComponents[1];

            // get access token for the Graph
            string authority = String.Format("https://login.microsoftonline.com/{0}", domain);
            string clientId = "be451d23-1a73-44c9-896a-39918ccce25f";
            Uri redirectUri = new Uri("http://whatevs");
            string graphResourceId = "https://graph.windows.net";

            AuthenticationContext authContext = new AuthenticationContext(authority, new FileCache());
            AuthenticationResult authResult = null;
            try
            {
                // let's see if we have something viable already cached
                authResult = authContext.AcquireTokenSilentAsync(graphResourceId, clientId).Result;                
            }
            catch (Exception ex)
            {
                if (ex.InnerException is AdalException)
                {
                    // nope. Let's get the user to authenticate
                    if (((AdalException)ex.InnerException).ErrorCode == "failed_to_acquire_token_silently")
                    {
                        authResult = authContext.AcquireTokenAsync(graphResourceId, clientId, redirectUri, new PlatformParameters(PromptBehavior.Always)).Result;
                    }
                    else
                    {
                        // An unexpected error occurred.
                        string message = ex.Message;
                        if (ex.InnerException != null)
                        {
                            message += "Inner Exception : " + ex.InnerException.Message;
                        }
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(message);
                        Console.ResetColor();
                        commandString = Console.ReadLine();
                        return;
                    }
                }
            }

            // Create an Account instance for the manager of the org we want to analyze
            Account manager = Account.CreateAsync(commandString, authResult.AccessToken).Result;
            // load up the cache of names/countries couples already inferred in the past
            GenderizeProxy.DeserializeCache();
            // infer the gender for the entire organization under the current manager
            manager.GetGenderMix(authResult.AccessToken);
            // save the cache to disk, just in case we added new entries
            GenderizeProxy.SerializeCache();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("{0}'s org has {1} males and {2} females",commandString,manager.Males,manager.Females);
            Console.WriteLine("That's a {0}% of males, {1}% of females.", manager.Males * 100 / (manager.Males + manager.Females), manager.Females * 100 / (manager.Males + manager.Females));
            if (manager.Undefined != 0)
            { Console.WriteLine("Those figures do not include {0} individuals as we were unable to assess gender from the first name only", manager.Undefined); }
            if (manager.Contacts != 0)
            {
                Console.WriteLine("We found {0} individuals without UPN.", manager.Contacts);
            }

            // save the results to file
            File.WriteAllText(string.Format("allResults_{0}.csv",commandString),manager.PrintOrganization());
            commandString = Console.ReadLine();
        }
    }
}
