using System;
using System.Collections.Generic;
using System.Text;
using CloudSMLib;


namespace CSMLibraryTester
{
    class Program
    {
        static void Main(string[] args)
        {
            CloudSM csm = new CloudSM();
            csm.HostName = "sm1s.saas.ca.com";
            csm.UserName = "user@test.com";
            csm.Password = "password";
            csm.ResponseFormat = "XML";

            ServiceRequest srq = new ServiceRequest();
            srq.ticket_description = "Summary";
            srq.description_long = "New Details";
            srq.ccti_class = "Server";
            srq.ccti_category = "Maintenance";
            srq.requester_name = "userID";

            Results results = csm.logServiceRequest(srq);

            //** List contacts
            //Results results = csm.listContacts("userID");

            //** Update Service Request **
            //srq.ticket_identifier = "100-23405";
            //srq.description_long = "Details Updated";
            //Results results = csm.updateServiceRequest(srq);

            //** Add Worklog **
            //Worklog worklog = new Worklog();
            //worklog.work_description = "Some updates";
            //worklog.ticket_identifier = "100-23429";
            //Results results = csm.addWorklog(worklog);

            Console.WriteLine(results.statusCode);
            Console.WriteLine(results.responseText);
            Console.ReadKey();
        }        
    }
}
