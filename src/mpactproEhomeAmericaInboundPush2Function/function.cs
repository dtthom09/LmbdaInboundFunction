using Amazon.Lambda.Core;
using BrickBridge.Models;
using PodioCore.Models;
using PodioCore.Items;
using PodioCore.Utils.ItemFields;
using System;
using System.Collections.Generic;
using System.Linq;
using BrickBridge;
using System.Text.RegularExpressions;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace mpactproEhomeAmericaInboundPush2Function
{
    public class Function:saasafrasLambdaBaseFunction.Function
    {
        private int GetFieldId(string key)
        {
            var field = _deployedSpaces[key];
            return int.Parse(field);
        }
        private int GetFieldIdEhome(string key)
        {
            var field = _deployedSpacesEhome[key];
            return int.Parse(field);
        }

        public static string StripHTML(string input) => Regex.Replace(input, "<.*?>", String.Empty);
        private Dictionary<string, string> _deployedSpaces;
        private Dictionary<string, string> _deployedSpacesEhome;

        public override async System.Threading.Tasks.Task InnerHandler(RoutedPodioEvent e, ILambdaContext lambda_ctx)
        {
            lambda_ctx.Logger.LogLine("EHome Data Status = Push 2");
            lambda_ctx.Logger.LogLine($"Podio Routed Event Version and AppId: {e.version}, {e.appId}");
            lambda_ctx.Logger.LogLine($"Lambda_ctx: {lambda_ctx.Identity}");
            System.Environment.SetEnvironmentVariable("PODIO_PROXY_URL", Config.PODIO_PROXY_URL);
            System.Environment.SetEnvironmentVariable("BBC_SERVICE_URL", Config.BBC_SERVICE_URL);
            System.Environment.SetEnvironmentVariable("BBC_SERVICE_API_KEY", Config.BBC_SERVICE_API_KEY);

            string url = Config.LOCKER_URL;
            string url2 = Config.BBC_SERVICE_URL;
            string key = Config.BBC_SERVICE_API_KEY;
            // eHome Landing Dictionary
            EhomeDictionary dict = new EhomeDictionary();
            _deployedSpacesEhome = dict.Dictionary;
            // Authenticate
            if (e.appId != null)
                lambda_ctx.Logger.LogLine($"AppId: |{e.appId}|");
            if (e.version != null)
                lambda_ctx.Logger.LogLine($"Version: |{e.version}|");
            if (e.clientId != null)
                lambda_ctx.Logger.LogLine($"clientId |{e.clientId}|");
            var app = "mpactprobeta";
            var version = "3.0";
            var level = "admin";
            var factory = new AuditedPodioClientFactory(app, version, level, level);
            var podioClient = factory.Client();

            // Get revision from Podio update
            var revision = await podioClient.GetRevisionDifference(Convert.ToInt32(e.podioEvent.item_id), e.currentItem.CurrentRevision.Revision - 1, e.currentItem.CurrentRevision.Revision);
            var fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|eHome Data Status");

            // If eHome Data Status was updated to Push 2
            if (e.currentItem.Field<CategoryItemField>(fieldId).Options.First().Text == "Push 2" && revision.First().FieldId == fieldId)
            {              
                var functionName = "mpactproEhomeAmericaInboundPush2Function";
                var uniqueId = e.currentItem.ItemId.ToString();
                var client = new BbcServiceClient(url, key);
                var lockValue = await client.LockFunction(functionName, uniqueId);
                try
                {
                    if (string.IsNullOrEmpty(lockValue))
                    {
                        lambda_ctx.Logger.LogLine($"Failed to acquire lock for {functionName} and id {uniqueId}");
                        return;
                    }
                    lambda_ctx.Logger.LogLine($"Lock Value: {lockValue}");

                    var appId = GetFieldIdEhome("*eHome America Landing Space|eHome America Profile Dictionary");
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Podio ID");
                    
                    // Search eHome America Profile Dictionary for client's Podio ID
                    var searchService = new PodioCore.Services.SearchService(podioClient);
                    var eHomeProfileDictionarySearch = await searchService.SearchInApp(appId, e.currentItem.Field<TextItemField>(fieldId).Value);

                    // Get item from eHome Dictionary
                    var ehomeProfileDictionaryGetItem = await podioClient.GetItem(eHomeProfileDictionarySearch.First().Id);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Profile Dictionary|Environment ID");
                    var envId = ehomeProfileDictionaryGetItem.Field<TextItemField>(fieldId).Value;
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Profile Dictionary|Client ID");
                    var clientId = ehomeProfileDictionaryGetItem.Field<TextItemField>(fieldId).Value;
                    lambda_ctx.Logger.LogLine($"clientId: {clientId}");
                    lambda_ctx.Logger.LogLine($"envId: {envId}");

                    // Create service and Get deployments 
                    lambda_ctx.Logger.LogLine($"Url: {url2}");

                    var bbcservice = new BbcServiceClient(url2, key);
                    var envs = await bbcservice.GetEnvironment(clientId, envId);
                    //var deployments = await bbcservice.GetDeployments(clientId, envId);//, "mpactprobeta");
                    var deployment = envs.deployments.First(); // TODO: find better way of getting app than first()
                    _deployedSpaces = deployment.deployedSpaces;

                    // Creation of eHome America Lead && setting all of the fields !
                    lambda_ctx.Logger.LogLine($"Creating eHome Item");
                    var ehomeItem = new Item();

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Are you a Veteran?");
                    var catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Veteran");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value == "Yes")
                        catField.OptionText = e.currentItem.Field<TextItemField>(fieldId).Value;
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "No")
                        catField.OptionText = e.currentItem.Field<TextItemField>(fieldId).Value;
                    else
                        catField.OptionText = "Not Available";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Are you active Military?");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Active Military");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value == "Yes")
                        catField.OptionText = e.currentItem.Field<TextItemField>(fieldId).Value;
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "No")
                        catField.OptionText = e.currentItem.Field<TextItemField>(fieldId).Value;
                    else
                        catField.OptionText = "Not Available";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|English Proficiency Level");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|English Speaking");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value == "Household is Limited English Proficient")
                        catField.OptionText = "a. Household is Limited English Proficient";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Household is not Limited English Proficient")
                        catField.OptionText = "b . Household is not Limited English Proficient";
                    else
                        catField.OptionText = "c. Chose not to respond";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Current Household");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Current Household Type");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value == "Rent")
                        catField.OptionText = "1.  Rent";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Homeless")
                        catField.OptionText = "2.  Homeless";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Not Paying Rent")
                        catField.OptionText = "4.  Not Paying Rent";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Homeowner with Mortage Paid Off")
                        catField.OptionText = "5.  Homeowner with Mortage Paid Off";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Disability");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Disability");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value == "Yes")
                        catField.OptionText = "Yes";
                    else
                        catField.OptionText = "No";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Ethnicity");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Ethnicity");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value == "Hispanic")
                        catField.OptionText = "a.  Hispanic";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Not Hispanic")
                        catField.OptionText = "b.  Not Hispanic";
                    else
                        catField.OptionText = "c.  Chose not to respond";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Gender");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Gender");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value == "Male")
                        catField.OptionText = "Male";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Female")
                        catField.OptionText = "Female";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Have you met with a lender about getting a home loan?");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Met Lender");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value == "Yes")
                        catField.OptionText = "Yes";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "No")
                        catField.OptionText = "No";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Highest Level of Education");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Highest Level of Education");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value == "Below High School Diploma")
                        catField.OptionText = "1. Below High School Diploma";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "High School Diploma or Equivalent")
                        catField.OptionText = "2. High School Diploma or Equivalent";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Two-Year College")
                        catField.OptionText = "3. Two-Year College";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Bachelors Degree")
                        catField.OptionText = "4. Bachelors Degree";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Masters Degree")
                        catField.OptionText = "5. Masters Degree";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Above Masters Degree")
                        catField.OptionText = "6. Above Masters Degree";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Household Type");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Household Type");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value == "Female headed single parent household")
                        catField.OptionText = "1. Female headed single parent household";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Male headed single parent household")
                        catField.OptionText = "2. Male headed single parent household";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Single adult")
                        catField.OptionText = "3. Single adult";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Two or more unrelated adults")
                        catField.OptionText = "4. Two or more unrelated adults";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Married with children")
                        catField.OptionText = "5. Married with children";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Married without children")
                        catField.OptionText = "6. Married without children";
                    else
                        catField.OptionText = "7. Other";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Marital Status");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Marital Status");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value == "Single")
                        catField.OptionText = "Single";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Married")
                        catField.OptionText = "Married";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Divorced")
                        catField.OptionText = "Divorced";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Separated")
                        catField.OptionText = "Separated";
                    else if (e.currentItem.Field<TextItemField>(fieldId).Value == "Widowed")
                        catField.OptionText = "Widowed";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Race");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Race");
                    switch (e.currentItem.Field<TextItemField>(fieldId).Value)
                    {
                        case "American Indian/Alaskan Native":
                            catField.OptionText = "a. American Indian/Alaskan Native";
                            break;
                        case "Asian":
                            catField.OptionText = "b. Asian";
                            break;
                        case "Black or African American":
                            catField.OptionText = "c. Black or African American";
                            break;
                        case "Native Hawaiian or Other Pacific Islander":
                            catField.OptionText = "d. Native Hawaiian or Other Pacific Islander";
                            break;
                        case "White":
                            catField.OptionText = "e. White";
                            break;
                        case "American Indian or Alaska Native and White":
                            catField.OptionText = "f. American Indian or Alaska Native and White";
                            break;
                        case "Asian and White":
                            catField.OptionText = "g. Asian and White";
                            break;
                        case "Black or African American and White":
                            catField.OptionText = "h. Black or African American and White";
                            break;
                        case "American Indian or Alaska Native and Black or African American":
                            catField.OptionText = "i. American Indian or Alaska Native and Black or African American";
                            break;
                        case "Other multiple race":
                        case "Other":
                            catField.OptionText = "j. Other multiple race";
                            break;
                        default:
                            catField.OptionText = "k. Chose not to respond";
                            break;
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|U.S. Citizen");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|U.S. Citizen");
                    switch (e.currentItem.Field<TextItemField>(fieldId).Value)
                    {
                        case "Yes":
                            catField.OptionText = "4 - Myself and parents and grandparents were all born inside US";
                            break;
                        case "No":
                            catField.OptionText = "3 - I was born outside the US";
                            break;
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|I am taking this course to:");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Course Reason");
                    switch (e.currentItem.Field<TextItemField>(fieldId).Value)
                    {
                        case "Meet my lender's qualifications":
                            catField.OptionText = "Meet my lender's qualifications";
                            break;
                        case "Become more educated about the homebuying process":
                            catField.OptionText = "Become more educated about the homebuying process";
                            break;
                        case "Determing if owning a home is the right decision":
                            catField.OptionText = "Determing if owning a home is the right decision";
                            break;
                        case "Learn how much I can spend on purchasing a home":
                            catField.OptionText = "Learn how much I can spend on purchasing a home";
                            break;
                        default:
                            catField.OptionText = "Meet my lender's qualifications";
                            break;
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Loan Type");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Rural Area Status");
                    var ruralStatus = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Education Client Type");
                    var educationClientType = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Loan Type");
                    switch (e.currentItem.Field<TextItemField>(fieldId).Value)
                    {
                        case "Conventional":
                            catField.OptionText = "Conventional";
                            ruralStatus.OptionText = "a. Household lives in a rural area";
                            educationClientType.OptionText = "Facilitated Education";
                            break;
                        case "FHA":
                            catField.OptionText = "FHA";
                            ruralStatus.OptionText = "a. Household lives in a rural area";
                            educationClientType.OptionText = "Facilitated Education";
                            break;
                        case "USDA":
                            catField.OptionText = "USDA";
                            ruralStatus.OptionText = "b. Household does not live in a rural area";
                            educationClientType.OptionText = "USDA Follow-Up";
                            break;
                        case "Neighborhood Stabilization Program (NSP)":
                            catField.OptionText = "Neighborhood Stabilization Program (NSP)";
                            ruralStatus.OptionText = "a. Household lives in a rural area";
                            educationClientType.OptionText = "Facilitated Education";
                            break;
                        case "Down Payment Assistance":
                            catField.OptionText = "Down Payment Assistance";
                            ruralStatus.OptionText = "a. Household lives in a rural area";
                            educationClientType.OptionText = "Facilitated Education";
                            break;
                        default:
                            catField.OptionText = "Other";
                            ruralStatus.OptionText = "a. Household lives in a rural area";
                            educationClientType.OptionText = "Facilitated Education";
                            break;
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|How did you hear about eHome America?");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Hear About eHome");
                    switch (e.currentItem.Field<TextItemField>(fieldId).Value)
                    {
                        case "Advertisement":
                            catField.OptionText = "Advertisement";
                            break;
                        case "Housing Counseling Agency":
                            catField.OptionText = "Housing Counseling Agency";
                            break;
                        case "Lender":
                            catField.OptionText = "Lender";
                            break;
                        case "Real Estate Professional":
                            catField.OptionText = "Real Estate Professional";
                            break;
                        case "Website":
                            catField.OptionText = "Website";
                            break;
                        case "Word of Mouth":
                            catField.OptionText = "Word of Mouth";
                            break;
                        case "Zillow":
                            catField.OptionText = "Zillow";
                            break;
                        case "Downpaymentresource.com":
                            catField.OptionText = "Downpaymentresource.com";
                            break;
                        default:
                            catField.OptionText = "Other";
                            break;
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Are you a First-Time Homebuyer?");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|First Time Homebuyer");
                    switch (e.currentItem.Field<TextItemField>(fieldId).Value)
                    {
                        case "No":
                            catField.OptionText = "No";
                            break;
                        default:
                            catField.OptionText = "Yes";
                            break;
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Have you been pre-approved for a loan up to a certain amount?");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Approved Loan");
                    switch (e.currentItem.Field<TextItemField>(fieldId).Value)
                    {
                        case "Yes":
                            catField.OptionText = "Yes";
                            break;
                        default:
                            catField.OptionText = "No";
                            break;
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Phone");
                    var phoneItemField = ehomeItem.Field<PhoneItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Client Phone Number");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                    {
                        phoneItemField.Value = new List<EmailPhoneFieldResult>
                    { new EmailPhoneFieldResult { Type = "other", Value = e.currentItem.Field<TextItemField>(fieldId).Value} };
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Total Annual Family Income");
                    var numericItemField = ehomeItem.Field<NumericItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Total Annual Family Income");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                    {
                        var income = e.currentItem.Field<TextItemField>(fieldId).Value.Replace(",", "");
                        income = StripHTML(income);
                        numericItemField.Value = Convert.ToDouble(income);
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Number in Household");
                    numericItemField = ehomeItem.Field<NumericItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Number in Household");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                    {
                        var income = e.currentItem.Field<TextItemField>(fieldId).Value.Replace(",", "");
                        income = StripHTML(income);
                        numericItemField.Value = Convert.ToDouble(income);
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|If you have been pre-approved, how much were you pre-approved for?");
                    numericItemField = ehomeItem.Field<NumericItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Approved Loan Amount");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                    {
                        var income = e.currentItem.Field<TextItemField>(fieldId).Value.Replace(",", "");
                        income = StripHTML(income);
                        numericItemField.Value = Convert.ToDouble(income);
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Date of Birth");
                    var dateItemField = ehomeItem.Field<DateItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Date of Birth");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                    {
                        var date = StripHTML(e.currentItem.Field<TextItemField>(fieldId).Value);
                        var dateSplit = date.Split('-');
                        if (dateSplit[0].Count() < 2)
                            dateSplit[0] = 0 + dateSplit[0];
                        dateItemField.Start = Convert.ToDateTime(dateSplit[2] + "-" + dateSplit[0] + "-" + dateSplit[1]);
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Create Home Purchase Case");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    catField.OptionText = "No";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Completed");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    catField.OptionText = "New";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Are you a First-Time Homebuyer?");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    catField.OptionText = "No";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Completed eHome Course?");
                    catField = ehomeItem.Field<CategoryItemField>(fieldId);
                    catField.OptionText = "Registration";

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Legal First Name");
                    var textField = ehomeItem.Field<TextItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Client First Name");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value;

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Legal Last Name");
                    textField = ehomeItem.Field<TextItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Client Last Name");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value;

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Street Number");
                    textField = ehomeItem.Field<TextItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Street Number");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value;

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Street Name");
                    textField = ehomeItem.Field<TextItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Street Name");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value;

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|State");
                    textField = ehomeItem.Field<TextItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|State");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value;

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|County");
                    textField = ehomeItem.Field<TextItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|County");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value;

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|City");
                    textField = ehomeItem.Field<TextItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|City");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value;

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Zip Code");
                    textField = ehomeItem.Field<TextItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Zip");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value;

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|eHome Profile ID");
                    textField = ehomeItem.Field<TextItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|eHome Profile ID");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value;

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Lender Name");
                    textField = ehomeItem.Field<TextItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Lender Name");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value;

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Loan Officer's Name");
                    textField = ehomeItem.Field<TextItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Loan Officer Name");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value;

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Loan Officer's Phone Number");
                    phoneItemField = ehomeItem.Field<PhoneItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Loan Officer Phone Number");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                    {
                        phoneItemField.Value = new List<EmailPhoneFieldResult>
                    {   new EmailPhoneFieldResult { Type = "other", Value = StripHTML(e.currentItem.Field<TextItemField>(fieldId).Value) }     };
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Loan Officer's Fax Number");
                    phoneItemField = ehomeItem.Field<PhoneItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Loan Officer Fax Number");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                    {
                        phoneItemField.Value = new List<EmailPhoneFieldResult>
                    { new EmailPhoneFieldResult { Type = "other", Value = StripHTML(e.currentItem.Field<TextItemField>(fieldId).Value) } };
                    }

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Household Family Size");
                    numericItemField = ehomeItem.Field<NumericItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Number in Household");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                        numericItemField.Value = Convert.ToDouble(StripHTML(e.currentItem.Field<TextItemField>(fieldId).Value));

                    fieldId = GetFieldId("3. Home Purchase|eHome America Lead|Email Address");
                    var emailItemField = ehomeItem.Field<EmailItemField>(fieldId);
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Client Email Address");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null)
                    {
                        emailItemField.Value = new List<EmailPhoneFieldResult>
                    { new EmailPhoneFieldResult { Type = "other", Value = e.currentItem.Field<TextItemField>(fieldId).Value } };
                    }

                    appId = GetFieldId("3. Home Purchase|eHome America Lead");
                    var itemId = await podioClient.CreateItem(ehomeItem, appId, false);
                    lambda_ctx.Logger.LogLine($"eHome item: {itemId} has been created :)");

                    var landingSpace = new Item { ItemId = e.currentItem.ItemId };

                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Local Item ID");
                    textField = landingSpace.Field<TextItemField>(fieldId);
                    textField.Value = itemId.ToString();

                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Local Item Status");
                    catField = landingSpace.Field<CategoryItemField>(fieldId);
                    catField.OptionText = "Item Created";

                    await podioClient.UpdateItem(landingSpace, true);
                    lambda_ctx.Logger.LogLine($"Local Item ID has been set");
                }
                catch (Exception ex)
                {
                    lambda_ctx.Logger.LogLine($"{ex}");
                    var errorItem = new Item();

                    fieldId = GetFieldIdEhome("*eHome America Landing Space|Error Log|Error Log");
                    var textField = errorItem.Field<TextItemField>(fieldId);
                    textField.Value = ex.Message.ToString();

                    fieldId = GetFieldIdEhome("*eHome America Landing Space|Error Log|Client Name");
                    textField = errorItem.Field<TextItemField>(fieldId);
                    var clientFirstNameFieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Client First Name");
                    fieldId = GetFieldIdEhome("*eHome America Landing Space|eHome America Landing Space|Client Last Name");
                    if (e.currentItem.Field<TextItemField>(fieldId).Value != null && e.currentItem.Field<TextItemField>(clientFirstNameFieldId).Value != null)
                        textField.Value = e.currentItem.Field<TextItemField>(fieldId).Value + ", " + e.currentItem.Field<TextItemField>(clientFirstNameFieldId).Value;

                    var appId = GetFieldIdEhome("*eHome America Landing Space|Error Log");
                    await podioClient.CreateItem(errorItem, appId, true);
                }
                finally
                {
                    await client.UnlockFunction(functionName, uniqueId, lockValue);
                }
            }
            else
                lambda_ctx.Logger.LogLine("Failed if statement");
        }
    }
}