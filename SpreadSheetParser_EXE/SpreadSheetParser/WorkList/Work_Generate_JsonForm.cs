﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpreadSheetParser
{
    public partial class Work_Generate_JsonForm : Form
    {
        Work_Generate_Json _pWork;

        public Work_Generate_JsonForm()
        {
            InitializeComponent();
        }

        public void DoInit(Work_Generate_Json pWork)
        {
            _pWork = null;

            checkBox_OpenFolder_AfterBuild.Checked = pWork.bOpenPath_AfterBuild_CSharp;
            textBox_Path.Text = pWork.strExportPath;

            _pWork = pWork;
        }

        private void checkBox_OpenFolder_AfterBuild_CheckedChanged(object sender, EventArgs e)
        {
            if (_pWork == null)
                return;

            _pWork.bOpenPath_AfterBuild_CSharp = checkBox_OpenFolder_AfterBuild.Checked;
        }

        private void Button_OpenPath_Click(object sender, EventArgs e)
        {
            _pWork.DoOpenPath(textBox_Path.Text);
        }

        private void button_SavePath_Click(object sender, EventArgs e)
        {
            if (_pWork == null)
                return;

            if (_pWork.DoShowFolderBrowser_And_SavePath(false, ref textBox_Path))
                _pWork.strExportPath = textBox_Path.Text;
        }

        private void button_SaveAndClose_Click(object sender, EventArgs e)
        {
            _pWork.DoAutoSaveAsync();
            Close();
        }
    }


    [System.Serializable]
    public class Work_Generate_Json : WorkBase
    {
        public string strExportPath;
        public bool bOpenPath_AfterBuild_CSharp;

        protected override void OnCreateInstance(out Type pFormType, out Type pType)
        {
            pFormType = typeof(Work_Generate_JsonForm);
            pType = GetType();
        }

        public override string GetDisplayString()
        {
            return "Generate Json";
        }

        public override void DoWork(CodeFileBuilder pCodeFileBuilder, IEnumerable<SaveData_Sheet> listSheetData)
        {
            foreach (var pSheet in listSheetData)
            { 
                JsonFormat pJson = new JsonFormat();
                JsonInstance pJsonInstance = new JsonInstance();

                Dictionary<int, string> mapMemberName = new Dictionary<int, string>();
                Dictionary<int, string> mapMemberType = new Dictionary<int, string>();
                int iColumnStartIndex = -1;
                
                pSheet.ParsingSheet(
                    (IList<object> listRow, string strText, int iRowIndex, int iColumnIndex) =>
                    {
                        if (strText.Contains(':'))
                        {
                            if (mapMemberName.ContainsKey(iColumnIndex))
                                return;

                            string[] arrText = strText.Split(':');
                            mapMemberName.Add(iColumnIndex, arrText[0]);
                            mapMemberType.Add(iColumnIndex, arrText[1]);

                            if (iColumnStartIndex == -1)
                                iColumnStartIndex = iColumnIndex;

                            return;
                        }

                        if (iColumnIndex != iColumnStartIndex)
                            return;

                        pJsonInstance = new JsonInstance();
                        pJson.listInstance.Add(pJsonInstance);

                        for (int i = iColumnIndex; i < listRow.Count; i++)
                        {
                            if(mapMemberName.ContainsKey(i))
                                pJsonInstance.listMember.Add(new JsonMember(mapMemberName[i], mapMemberType[i], (string)listRow[i]));
                        }
                    });

                JsonSaveManager.SaveData(pJson, $"{GetRelative_To_AbsolutePath()}{strExportPath}/{pSheet.strSheetName}.json");
            }
        }

        public override void DoWorkAfter()
        {
            if (bOpenPath_AfterBuild_CSharp)
                DoOpenPath(strExportPath);
        }

        protected override void OnShowForm(Form pFormInstance)
        {
            Work_Generate_JsonForm pForm = (Work_Generate_JsonForm)pFormInstance;
            pForm.DoInit(this);
        }
    }

}
