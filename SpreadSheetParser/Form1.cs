﻿using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.CodeDom;

namespace SpreadSheetParser
{
    public partial class SpreadSheetParser : Form
    {
        // If modifying these scopes, delete your previously saved credentials
        // at ~/.credentials/sheets.googleapis.com-dotnet-quickstart.json
        static string[] Scopes = { SheetsService.Scope.SpreadsheetsReadonly };
        static string ApplicationName = "Google Sheets API .NET Quickstart";

        public SpreadSheetParser()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void button_Connect_Click(object sender, EventArgs e)
        {
            UserCredential credential;

            using (var stream =
                new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
            {
                // The file token.json stores the user's access and refresh tokens, and is created
                // automatically when the authorization flow completes for the first time.
                string credPath = "token.json";
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
                Console.WriteLine("Credential file saved to: " + credPath);
            }

            // Create Google Sheets API service.
            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            // Define request parameters.
            string spreadsheetId = "16E4yPh8VkPfL-VeaBmcJ1vrZc36E3jFchanlEN_Apd8";
            var pTest = service.Spreadsheets.Get(spreadsheetId);
            checkedListBox_TableList.Items.Clear();

            try
            {
                var TestResponse = pTest.Execute();
                for (int i = 0; i < TestResponse.Sheets.Count; i++)
                    checkedListBox_TableList.Items.Add(new SheetWrapper(TestResponse.Sheets[i]));
            }
            catch (Exception pException)
            {
            }

            string strSheetName = "MonsterData";
            string range = "!A2:C";

            SpreadsheetsResource.ValuesResource.GetRequest request =
                    service.Spreadsheets.Values.Get(spreadsheetId, strSheetName + range);

            ValueRange response = request.Execute();

            IList<IList<Object>> values = response.Values;
            if (values != null && values.Count > 0)
            {
                //List<MyTypeBuilder.FieldInfo> listFieldInfo = new List<MyTypeBuilder.FieldInfo>();
                //listFieldInfo.Add(new MyTypeBuilder.FieldInfo("intTest", typeof(int)));

                //System.Type pType = MyTypeBuilder.CompileResultType(listFieldInfo);
            }
            else
            {
                Console.WriteLine("No data found.");
            }
            Console.Read();
        }

        private void button_StartParsing_Click(object sender, EventArgs e)
        {

        }

    }

    public class SheetWrapper
    {
        public Sheet pSheet { get; private set; }

        public SheetWrapper(Sheet pSheet)
        {
            this.pSheet = pSheet;
        }

        public override string ToString()
        {
            if (pSheet == null)
                return "Error";

            return pSheet.Properties.Title;
        }
    }

}
