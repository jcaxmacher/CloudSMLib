using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;

namespace CloudSMLib
{
    public class CloudSM
    {
        #region Private data

        // For parsing mime/multipart http responses
        private Regex ignorePattern = new Regex(@"(^--MIMEBoundary.*$)|(^Content-[^:]+:.*$)|(^$)");

        // Mapping of web service method to endpoint (and whether method requires a special content-type)
        [ComVisible(false)]
        private Dictionary<string, string[]> actionUrlMap = new Dictionary<string, string[]>
        {
            { "listContacts", new string[]{"https://{0}/servicedesk/webservices/Contact.ContactHttpSoap11Endpoint/", ""}},
            { "logServiceRequest", new string[]{"https://{0}/servicedesk/webservices/ServiceRequest.ServiceRequestHttpSoap11Endpoint/", "special"}},
            { "updateServiceRequest", new string[]{"https://{0}/servicedesk/webservices/ServiceRequest.ServiceRequestHttpSoap11Endpoint/", "special"}},
            { "addWorklog", new string[]{"https://{0}/servicedesk/webservices/ServiceRequest.ServiceRequestHttpSoap12Endpoint/", "special"}}
        };

        // Mapping to check if xml tag from response is in the results class
        [ComVisible(false)]
        private Dictionary<string, int> resultsFields = new Dictionary<string, int>
        {
            {"errors", 0},
            {"notes", 0},
            {"resourcename", 0},
            {"responsebean", 0},
            {"responseformat", 0},
            {"responsestatus", 0},
            {"responsetext", 0},
            {"statuscode", 0},
            {"statusmessage", 0},
            {"warnings", 0},
        };

        #endregion

        #region Public Interface

        public string HostName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string ResponseFormat { get; set; }

        public CloudSM()
        {
            ResponseFormat = "JSON";
        }

        public Results logServiceRequest(ServiceRequest srq)
        {
            LogSrqEnvelope envelope = wrapLogSrq(srq);
            string requestPayload = serialize(envelope);
            string responsePayload = makeRequest("logServiceRequest", requestPayload);
            return parseResultsXml(responsePayload);
        }

        public Results updateServiceRequest(ServiceRequest srq)
        {
            UpdateSrqEnvelope envelope = wrapUpdateSrq(srq);
            string requestPayload = serialize(envelope);
            string responsePayload = makeRequest("updateServiceRequest", requestPayload);
            return parseResultsXml(responsePayload);
        }

        public Results addWorklog(Worklog worklog)
        {
            AddWorklogEnvelope envelope = wrapAddWorklog(worklog);
            string requestPayload = serialize(envelope);
            string responsePayload = makeRequest("addWorklog", requestPayload);
            return parseResultsXml(responsePayload);
        }

        public Results listContacts(string searchText)
        {
            ListContactsEnvelope envelope = wrapListContacts(searchText);
            string requestPayload = serialize(envelope);
            string responsePayload = makeRequest("listContacts", requestPayload);
            return parseResultsXml(responsePayload);
        }

        #endregion

        #region Http/Xml/Multipart helper methods

        [ComVisible(false)]
        private Results parseResultsXml(string xmlResponse)
        {
            Results results = new Results();

            // Find the return xml element within the response
            using (XmlReader reader = XmlReader.Create(new StringReader(xmlResponse)))
            {
                while (reader.Read())
                {
                    string elementName = reader.Name.ToLower();
                    string[] elementNameParts = elementName.Split(':');
                    string tagName = "";

                    // Strip out namespace
                    if (elementNameParts.Length > 1)
                    {
                        tagName = elementNameParts[1];
                    }
                    else
                    {
                        tagName = elementNameParts[0];
                    }
                    
                    // Add values to results
                    if (reader.IsStartElement() && resultsFields.ContainsKey(tagName))
                    {
                        reader.Read();
                        switch (tagName)
                        {
                            case "errors":
                                results.errors = reader.Value.Trim();
                                break;
                            case "notes":
                                results.notes = reader.Value.Trim();
                                break;
                            case "resourcename":
                                results.resourceName = reader.Value.Trim();
                                break;
                            case "responsebean":
                                results.responseBean = reader.Value.Trim();
                                break;
                            case "responseformat":
                                results.responseFormat = reader.Value.Trim();
                                break;
                            case "responsestatus":
                                results.responseStatus = reader.Value.Trim();
                                break;
                            case "responsetext":
                                results.responseText = reader.Value.Trim();
                                break;
                            case "statusmessage":
                                results.statusMessage = reader.Value.Trim();
                                break;
                            case "statuscode":
                                results.statusCode = reader.Value.Trim();
                                break;
                            case "warnings":
                                results.warnings = reader.Value.Trim();
                                break;
                        }
                    }
                }
            }
            return results;
        }

        [ComVisible(false)]
        private string makeRequest(string soapAction, string payload)
        {
            // For bypassing ssl certificate checks when using Fiddler
            // ServicePointManager.ServerCertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(delegate { return true; });

            // Prepare web request with correct url
            string[] endpointInfo = actionUrlMap[soapAction];
            string url = endpointInfo[0];
            Uri uri = new Uri(String.Format(url, HostName));
            WebRequest request = WebRequest.Create(uri);
            HttpWebRequest httpRequest = (HttpWebRequest)request;

            // Configure http request
            httpRequest.Method = "POST";
            httpRequest.Headers.Add("SOAPAction", "urn:" + soapAction);
            httpRequest.UserAgent = "Mozilla/5.0 (compatible; CA GIS Web Service Client)";
            // Weird content-type workarounds for different web service methods
            if (endpointInfo[1] == "special")
            {
                httpRequest.ContentType = "application/soap+xml;charset=UTF-8;action=\"urn:" + soapAction + "\"";
            }
            else
            {
                httpRequest.ContentType = "text/xml;charset=UTF-8";
            }            

            // Write request payload
            using (Stream requestStream = httpRequest.GetRequestStream())
            using (StreamWriter streamWriter = new StreamWriter(requestStream))
            {
                streamWriter.Write(payload);
            }

            // Read response into string
            WebResponse response = httpRequest.GetResponse();
            string responseText = "";
            using (Stream responseStream = response.GetResponseStream())
            using (StreamReader streamReader = new StreamReader(responseStream))
            {
                responseText = streamReader.ReadToEnd();
            }
            
            if (response.ContentType.ToLower().Contains("multipart/related"))
            {
                return parseMultipart(responseText).Trim();
            }
            else
            {
                return responseText.Trim();
            }
        }

        [ComVisible(false)]
        private string parseMultipart(string responseText)
        {
            StringBuilder newResponseText = new StringBuilder();
            foreach (string line in responseText.Split('\n'))
            {
                if (!ignorePattern.Match(line).Success)
                {
                    newResponseText.Append(line);
                }
            }
            return newResponseText.ToString();
        }

        [ComVisible(false)]
        private string serialize(Envelope envelope)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.OmitXmlDeclaration = true;
            settings.Indent = true;
            XmlSerializer serializer = new XmlSerializer(envelope.GetType());

            using (StringWriter stream = new StringWriter())
            using (XmlWriter writer = XmlWriter.Create(stream, settings))
            {
                serializer.Serialize(writer, envelope);
                return stream.ToString();
            }
        }

        #endregion

        #region SOAP Envelop wrapper methods

        [ComVisible(false)]
        private ListContactsEnvelope wrapListContacts(string searchText)
        {
            ListContactsEnvelope envelope = new ListContactsEnvelope();
            envelope.Header = new Header();
            envelope.Body = new ListContactsBody();
            envelope.Body.listContacts = new ContactsWrapper();
            envelope.Body.listContacts.credentials = new Credentials(UserName, Password);
            envelope.Body.listContacts.extendedSettings = new ExtendedSettings(ResponseFormat);
            envelope.Body.listContacts.searchText = searchText;
            return envelope;
        }

        [ComVisible(false)]
        private AddWorklogEnvelope wrapAddWorklog(Worklog worklog)
        {
            AddWorklogEnvelope envelope = new AddWorklogEnvelope();
            envelope.Header = new Header();
            envelope.Body = new AddWorklogBody();
            envelope.Body.addWorklog = new WorklogWrapper();
            envelope.Body.addWorklog.credentials = new Credentials(UserName, Password);
            envelope.Body.addWorklog.extendedSettings = new ExtendedSettings(ResponseFormat);
            envelope.Body.addWorklog.workglogBean = worklog;
            return envelope;           
        }

        [ComVisible(false)]
        private LogSrqEnvelope wrapLogSrq(ServiceRequest srq)
        {
            LogSrqEnvelope envelope = new LogSrqEnvelope();
            envelope.Header = new Header();
            envelope.Body = new LogSrqBody();
            envelope.Body.logServiceRequest = new SrqWrapper();
            envelope.Body.logServiceRequest.credentials = new Credentials(UserName, Password);
            envelope.Body.logServiceRequest.extendedSettings = new ExtendedSettings(ResponseFormat);
            envelope.Body.logServiceRequest.srqBean = srq;
            return envelope;
        }

        [ComVisible(false)]
        private UpdateSrqEnvelope wrapUpdateSrq(ServiceRequest srq)
        {
            UpdateSrqEnvelope envelope = new UpdateSrqEnvelope();
            envelope.Header = new Header();
            envelope.Body = new UpdateSrqBody();
            envelope.Body.updateServiceRequest = new SrqWrapper();
            envelope.Body.updateServiceRequest.credentials = new Credentials(UserName, Password);
            envelope.Body.updateServiceRequest.extendedSettings = new ExtendedSettings(ResponseFormat);
            envelope.Body.updateServiceRequest.srqBean = srq;
            return envelope;
        }

        #endregion
    }

    #region SOAP Envelope request classes

    // Top level class for re-usable serialization
    [ComVisible(false)]
    public class Envelope { }

    [ComVisible(false)]
    [XmlRoot(Namespace = "http://www.w3.org/2003/05/soap-envelope",
     ElementName = "Envelope",
     IsNullable = false)]
    public class LogSrqEnvelope : Envelope
    {
        public Header Header { get; set; }
        public LogSrqBody Body { get; set; }
    }

    [ComVisible(false)]
    [XmlRoot(Namespace = "http://www.w3.org/2003/05/soap-envelope",
     ElementName = "Envelope",
     IsNullable = false)]
    public class UpdateSrqEnvelope : Envelope
    {
        public Header Header { get; set; }
        public UpdateSrqBody Body { get; set; }
    }

    [ComVisible(false)]
    [XmlRoot(Namespace = "http://www.w3.org/2003/05/soap-envelope",
     ElementName = "Envelope",
     IsNullable = false)]
    public class AddWorklogEnvelope : Envelope
    {
        public Header Header { get; set; }
        public AddWorklogBody Body { get; set; }
    }

    [ComVisible(false)]
    [XmlRoot(Namespace = "http://schemas.xmlsoap.org/soap/envelope/",
     ElementName = "Envelope",
     IsNullable = false)]
    public class ListContactsEnvelope : Envelope
    {
        public Header Header { get; set; }
        public ListContactsBody Body { get; set; }
    }

    [ComVisible(false)]
    public class Header { }

    [ComVisible(false)]
    public class LogSrqBody
    {
        [XmlElement(Namespace = "http://wrappers.webservice.appservices.core.inteqnet.com",
            ElementName = "logServiceRequest",
            IsNullable = false)]
        public SrqWrapper logServiceRequest { get; set; }
    }

    [ComVisible(false)]
    public class UpdateSrqBody
    {
        [XmlElement(Namespace = "http://wrappers.webservice.appservices.core.inteqnet.com",
            ElementName = "updateServiceRequest",
            IsNullable = false)]
        public SrqWrapper updateServiceRequest { get; set; }
    }

    [ComVisible(false)]
    public class AddWorklogBody
    {
        [XmlElement(Namespace = "http://wrappers.webservice.appservices.core.inteqnet.com",
            ElementName = "addWorklog",
            IsNullable = false)]
        public WorklogWrapper addWorklog { get; set; }
    }

    [ComVisible(false)]
    public class ListContactsBody
    {
        [XmlElement(Namespace = "http://wrappers.webservice.appservices.core.inteqnet.com",
            ElementName = "listContacts",
            IsNullable = false)]
        public ContactsWrapper listContacts { get; set; }
    }

    [ComVisible(false)]
    public class SrqWrapper
    {
        public Credentials credentials { get; set; }
        public ExtendedSettings extendedSettings { get; set; }
        public ServiceRequest srqBean { get; set; }
    }

    [ComVisible(false)]
    public class WorklogWrapper
    {
        public Credentials credentials { get; set; }
        public ExtendedSettings extendedSettings { get; set; }
        public Worklog workglogBean { get; set; }
    }

    [ComVisible(false)]
    public class ContactsWrapper
    {
        public Credentials credentials { get; set; }
        public ExtendedSettings extendedSettings { get; set; }
        [XmlElement(Namespace = "http://wrappers.webservice.appservices.core.inteqnet.com",
            ElementName = "searchText",
            IsNullable = false)]
        public string searchText { get; set; }
    }

    [ComVisible(false)]
    public class Credentials
    {
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string userName { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string userPassword { get; set; }
        public Credentials(string userName, string userPassword)
        {
            this.userName = userName;
            this.userPassword = userPassword;
        }
        public Credentials() { }
    }

    [ComVisible(false)]
    public class ExtendedSettings
    {
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string responseFormat { get; set; }
        public ExtendedSettings(string responseFormat)
        {
            this.responseFormat = responseFormat;
        }
        public ExtendedSettings() { }
    }

    public class Worklog
    {
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string item_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string row_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_identifier { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_type { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_actual_date { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_created_by { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_created_by_contact_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_created_date { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_description { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_modified_by { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_modified_by_contact_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_modified_date { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_time_spent { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_type { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_type_code { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string work_view_type { get; set; }
    }

    public class ServiceRequest
    {
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string affected_ci_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string affected_ci_identifier { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string affected_ci_name { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string assigned_contact_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string assigned_group_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string assigned_to_group_name { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string assigned_to_individual_name { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string case_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string cause { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ccti_category { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ccti_class { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ccti_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ccti_item { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ccti_type { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string description_long { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string parent_row_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string parent_ticket_identifier { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person1_address_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person1_alt_email { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person1_alt_phone { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person1_contact_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person1_lvl1_org_name { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person1_lvl2_org_name { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person1_lvl3_org_name { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person1_org_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person2_address_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person2_alt_email { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person2_alt_phone { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person2_contact_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person2_lvl1_org_name { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person2_lvl2_org_name { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person2_lvl3_org_name { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string person2_org_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string requested_for_name { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string requester_name { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string resolution { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string row_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string solution_used_from_item_case { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string solution_used_from_item_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string support_email_address { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_description { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_identifier { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_impact { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_impact_code { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_last_action_used_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_phase { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_priority { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_priority_code { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_reason_code { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_solution_id { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_source { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_source_code { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_status { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_urgency { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string ticket_urgency_code { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string vip_flag_person1 { get; set; }
        [XmlElement(Namespace = "http://beans.webservice.appservices.core.inteqnet.com/xsd")]
        public string vip_flag_person2 { get; set; }
    }

    #endregion

    #region SOAP Envelope response classes

    public class Results
    {
        public string errors { get; set; }
        public string notes { get; set; }
        public string resourceName { get; set; }
        public string responseBean { get; set; }
        public string responseFormat { get; set; }
        public string responseStatus { get; set; }
        public string responseText { get; set; }
        public string statusCode { get; set; }
        public string statusMessage { get; set; }
        public string warnings { get; set; }

        public Results()
        {
            errors = "";
            notes = "";
            resourceName = "";
            responseBean = "";
            responseFormat = "";
            responseStatus = "";
            responseText = "";
            statusCode = "";
            statusMessage = "";
            warnings = "";
        }


    }

    #endregion
}
