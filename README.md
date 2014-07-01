# CA Cloud Service Management web services .NET Client Library

This project provides a .NET 2.0, COM-interface enabled client library for
utilizing the CA Cloud Service Management SOAP web services API.

The following web service methods are currently supported:

  - ServiceRequest
    - logServiceRequest
    - updateServiceRequest
    - addWorklog
  - Contact
    - listContacts

## Usage

```
CloudSM csm = new CloudSM();
csm.HostName = "sm1s.saas.ca.com";
csm.UserName = "aaa@bbb.ccc";
csm.Password = "password";
csm.ResponseFormat = "XML"; // Defaults to JSON if not set

ServiceRequest srq = new ServiceRequest();
srq.ticket_description = "summary";
srq.description_long = "details";
srq.requester_name = "userid";
srq.ccti_class = "Password Reset";

Results results = csm.logServiceRequest(srq)
Console.WriteLine(results.statusCode);
Console.WriteLine(results.responseText);
Console.ReadKey();
```

Instantiate the CloudSM class, set the basic information and then call the
instance methods to log and update service requests, add worklogs to service
requests, and search for contacts.

## Notes

CA Cloud Service Management sends `multipart/related` HTTP responses.  The
parser for those responses is very naive and could possibly break if the
response format changes.  This is something to keep an eye on for application
upgrades.
