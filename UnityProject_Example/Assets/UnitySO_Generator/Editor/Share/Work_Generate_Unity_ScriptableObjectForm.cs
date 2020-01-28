﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using static SpreadSheetParser.TypeDataHelper;

#if !UNITY_EDITOR
using System.Windows.Forms;
#endif

namespace SpreadSheetParser
{
#if !UNITY_EDITOR
    public partial class Work_Generate_Unity_ScriptableObjectForm : Form
    {
        Work_Generate_Unity_ScriptableObject _pWork;

        public Work_Generate_Unity_ScriptableObjectForm()
        {
            InitializeComponent();
        }

        public void DoInit(Work_Generate_Unity_ScriptableObject pWork)
        {
            _pWork = null;
            
            checkBox_OpenFolder_AfterBuild.Checked = pWork.bOpenPath_AfterBuild_CSharp;
            textBox_EditorPath.Text = pWork.strUnityEditorPath;
            textBox_ExportPath.Text = pWork.strExportPath;

            _pWork = pWork;
        }

        private void Button_OpenPath_Click(object sender, EventArgs e)
        {
            _pWork.DoOpenPath(textBox_EditorPath.Text);
        }

        private void button_SavePath_EditorClick(object sender, EventArgs e)
        {
            if (_pWork == null)
                return;

            if (_pWork.DoShowFileBrowser_And_SavePath(true, ref textBox_EditorPath, (strFileName) => strFileName.Contains("Unity.exe"), "Unity 실행프로그램이 아닙니다"))
                _pWork.strUnityEditorPath = textBox_EditorPath.Text;
        }

        private void checkBox_OpenFolder_AfterBuild_CheckedChanged(object sender, EventArgs e)
        {
            if (_pWork == null)
                return;

            _pWork.bOpenPath_AfterBuild_CSharp = checkBox_OpenFolder_AfterBuild.Checked;
        }

        private void button_SavePath_ExportPath_Click(object sender, EventArgs e)
        {
            if (_pWork == null)
                return;

            if (_pWork.DoShowFolderBrowser_And_SavePath(false, ref textBox_ExportPath))
                _pWork.strExportPath = textBox_ExportPath.Text;
        }

        private void Button_OpenPath_ExportPath_Click(object sender, EventArgs e)
        {
            _pWork.DoOpenPath(textBox_ExportPath.Text);
        }

        private void button_SaveAndClose_Click(object sender, EventArgs e)
        {
            _pWork.DoAutoSaveAsync();
            Close();
        }
    }
#endif

    [System.Serializable]
    public class Work_Generate_Unity_ScriptableObject : WorkBase
    {
        const string const_strListData = "listData";

        public string strExportPath;
        public string strUnityProjectPath;
        public string strUnityEditorPath;
        public bool bOpenPath_AfterBuild_CSharp;

#if !UNITY_EDITOR
        protected override void OnCreateInstance(out Type pFormType, out Type pType)
        {
            pFormType = typeof(Work_Generate_Unity_ScriptableObjectForm);
            pType = GetType();
    }
#endif

        public override string GetDisplayString()
        {
            return "Generate Unity SO";
        }

        public override void DoWork(CodeFileBuilder pCodeFileBuilder, SpreadSheetConnector pConnector, IEnumerable<TypeData> listSheetData, System.Action<string> OnPrintWorkState)
        {
            CodeTypeDeclarationCollection arrTypes = pCodeFileBuilder.GetCodeTypeDeclarationCollection();
            List<CodeTypeDeclaration> listType = new List<CodeTypeDeclaration>();
            foreach (CodeTypeDeclaration pType in arrTypes)
                listType.Add(pType);

            CodeNamespace pNameSpace = new CodeNamespace();
            pNameSpace.Imports.Add(new CodeNamespaceImport("UnityEngine"));

            CodeTypeDeclaration pGlobalKeyEnum = listType.Where(p => p.Name == const_GlobalKey_EnumName).FirstOrDefault();
            HashSet<CodeTypeDeclaration> setExecutedType = new HashSet<CodeTypeDeclaration>();

            IEnumerable<CodeTypeDeclaration> listUnitySO = listType.Where(p => string.IsNullOrEmpty(p.Name) == false && p.IsClass);
            foreach (CodeTypeDeclaration pType in listUnitySO)
            {
                TypeData pSaveData = listSheetData.Where((pSaveDataSheet) => pSaveDataSheet.strFileName == pType.Name).FirstOrDefault();
                if (pSaveData == null)
                    continue;

                setExecutedType.Add(pType);
                Create_SO(pCodeFileBuilder, pNameSpace, pType, pSaveData);

                if (pSaveData.eType == ESheetType.Global)
                {
                    Create_GlobalSOContainer(pCodeFileBuilder, pNameSpace, pType, pGlobalKeyEnum, pSaveData);
                    setExecutedType.Add(pGlobalKeyEnum);
                }
                else
                {
                    Create_SOContainer(pCodeFileBuilder, pNameSpace, pType, pSaveData);
                }

                OnPrintWorkState?.Invoke($"UnitySO - Working SO {pType.Name}");
            }

            // Others
            pNameSpace.Types.Clear();
            IEnumerable<CodeTypeDeclaration> listOthers = listType.Where(p => string.IsNullOrEmpty(p.Name) == false && setExecutedType.Contains(p) == false);
            foreach (CodeTypeDeclaration pType in listOthers)
            {
                OnPrintWorkState?.Invoke($"UnitySO - Working Others {pType.Name}");
                pNameSpace.Types.Add(pType);
                setExecutedType.Add(pType);
            }

            if (pNameSpace.Types.Count != 0)
                pCodeFileBuilder.Generate_CSharpCode(pNameSpace, $"{GetRelative_To_AbsolutePath(strExportPath)}/Others");
        }

        private void Create_SO(CodeFileBuilder pCodeFileBuilder, CodeNamespace pNameSpace, CodeTypeDeclaration pType, TypeData pSaveData)
        {
            pType.AddBaseClass(nameof(UnityEngine.ScriptableObject));
            pNameSpace.Types.Clear();
            pNameSpace.Types.Add(pType);

            var listVirtualFieldOption = pSaveData.listFieldData.Where(pExportOption => pExportOption.bDeleteThisField_InCode == false && pExportOption.bIsVirtualField);
            foreach (var pVirtualField in listVirtualFieldOption)
                pType.AddField(pVirtualField);

            pCodeFileBuilder.Generate_CSharpCode(pNameSpace, $"{GetRelative_To_AbsolutePath(strExportPath)}/{pType.Name}");
        }

        private void Create_GlobalSOContainer(CodeFileBuilder pCodeFileBuilder, CodeNamespace pNameSpace, CodeTypeDeclaration pType, CodeTypeDeclaration pGlobalKeyEnumType, TypeData pSaveData)
        {
            CodeTypeDeclaration pContainerType;
            CodeMemberMethod pInitMethod;
            Create_SOContainer(pNameSpace, pType, out pContainerType, out pInitMethod);
            pNameSpace.Types.Add(pGlobalKeyEnumType);

            IEnumerable<FieldTypeData> listKeyField = pSaveData.listFieldData.Where(p => p.bIsKeyField);

            string strValueFieldName = "";
            IEnumerable<FieldTypeData> listRealField = pSaveData.listFieldData.Where(p => p.bIsKeyField == false);
            foreach (var pRealField in listRealField)
            {
                if(pRealField.strFieldName.ToLower().Contains(nameof(TypeDataHelper.EGlobalColumnType.Value).ToLower()))
                {
                    strValueFieldName = pRealField.strFieldName;
                    break;
                }
            }

            HashSet<string> setAlreadyExecute = new HashSet<string>();
            foreach (var pFieldData in listKeyField)
            {
                if (setAlreadyExecute.Contains(pFieldData.strFieldType))
                    continue;
                setAlreadyExecute.Add(pFieldData.strFieldType);

                string strFieldName = $"mapData_Type_Is_{pFieldData.strFieldType}";
                string strMemberType = $"Dictionary<{"EGlobalKey"}, {pFieldData.strFieldType}>";

                pContainerType.AddField(new FieldTypeData(strFieldName, strMemberType));
                Generate_CacheMethod_Global(pContainerType, pInitMethod, const_strListData, strFieldName, pFieldData.strFieldName, pFieldData.strFieldType, strValueFieldName);
            }

            pCodeFileBuilder.Generate_CSharpCode(pNameSpace, $"{GetRelative_To_AbsolutePath(strExportPath)}/{pContainerType.Name}");
        }


        private void Create_SOContainer(CodeFileBuilder pCodeFileBuilder, CodeNamespace pNameSpace, CodeTypeDeclaration pType, TypeData pSaveData)
        {
            CodeTypeDeclaration pContainerType;
            CodeMemberMethod pInitMethod;
            Create_SOContainer(pNameSpace, pType, out pContainerType, out pInitMethod);

            IEnumerable<FieldTypeData> listKeyField = pSaveData.listFieldData.Where(p => p.bIsKeyField);
            foreach (var pFieldData in listKeyField)
            {
                string strFieldName = "";
                string strMemberType = "";
                if (pFieldData.bIsOverlapKey)
                {
                    strFieldName = $"mapData_Key_Is_{pFieldData.strFieldName}";
                    strMemberType = $"Dictionary<{pFieldData.strFieldType}, List<{pType.Name}>>";
                }
                else
                {
                    strFieldName = $"mapData_Key_Is_{pFieldData.strFieldName}";
                    strMemberType = $"Dictionary<{pFieldData.strFieldType}, {pType.Name}>";
                }

                pContainerType.AddField(new FieldTypeData(strFieldName, strMemberType));
                Generate_CacheMethod(pContainerType, pInitMethod, const_strListData, strFieldName, pFieldData.strFieldName, pFieldData.bIsOverlapKey);
            }

            pCodeFileBuilder.Generate_CSharpCode(pNameSpace, $"{GetRelative_To_AbsolutePath(strExportPath)}/{pContainerType.Name}");
        }

        private void Create_SOContainer(CodeNamespace pNameSpace, CodeTypeDeclaration pType, out CodeTypeDeclaration pContainerType, out CodeMemberMethod pInitMethod)
        {
            pContainerType = new CodeTypeDeclaration(pType.Name + "_Container");
            pContainerType.AddBaseClass(nameof(UnityEngine.ScriptableObject));

            pNameSpace.Imports.Clear();
            pNameSpace.Imports.Add(new CodeNamespaceImport("System.Linq"));
            pNameSpace.Imports.Add(new CodeNamespaceImport("System.Collections.Generic"));
            pNameSpace.Imports.Add(new CodeNamespaceImport("UnityEngine"));
            pNameSpace.Types.Clear();
            pNameSpace.Types.Add(pContainerType);

            pContainerType.AddField(new FieldTypeData(const_strListData, $"List<{pType.Name}>"));
            pInitMethod = Generate_InitMethod(pContainerType, pType.Name);
        }

        private CodeMemberMethod Generate_InitMethod(CodeTypeDeclaration pContainerType, string strTypeName)
        {
            var pMethod = pContainerType.AddMethod($"DoInit");

            pMethod.Statements.Add(new CodeSnippetStatement("#if UNITY_EDITOR"));
            pMethod.Statements.Add(new CodeSnippetStatement("        listData.Clear();"));
            pMethod.Statements.Add(new CodeSnippetStatement("        Object[] arrObject = UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath(UnityEditor.AssetDatabase.GetAssetPath(this));"));
            pMethod.Statements.Add(new CodeSnippetStatement("        for (int i = 0; i < arrObject.Length; i++)"));
            pMethod.Statements.Add(new CodeSnippetStatement($"           listData.Add(({strTypeName})arrObject[i]);"));

            pMethod.Statements.Add(new CodeSnippetStatement("       UnityEditor.EditorUtility.SetDirty(this);"));
            pMethod.Statements.Add(new CodeSnippetStatement("#endif"));



            return pMethod;
        }

        private void Generate_CacheMethod_Global(CodeTypeDeclaration pContainerType, CodeMemberMethod pInitMethod, string strListDataName, string strMapFieldName, string strTypeFieldName, string strTypeName, string strValueFieldName)
        {
            string strMethodName = $"Init_{strMapFieldName}";
            var pMethod = pContainerType.AddMethod(strMethodName);
            pMethod.Attributes = MemberAttributes.Private | MemberAttributes.Final;

            CodeFieldReferenceExpression pCasheMemberReference =
                new CodeFieldReferenceExpression(
                new CodeThisReferenceExpression(), strMapFieldName);

            CodeTypeReferenceExpression pField_List = new CodeTypeReferenceExpression($"{strListDataName}");

            // 1. Where로 묶는다.
            CodeMethodInvokeExpression pMethod_CachingLocal = new CodeMethodInvokeExpression(
                pField_List, "Where", new CodeSnippetExpression($"x => x.{strTypeFieldName} == \"{strTypeName}\""));

            CodeVariableDeclarationStatement pGroupbyVariableDeclaration = new CodeVariableDeclarationStatement(
                "var", "arrLocal", pMethod_CachingLocal);

            pMethod.Statements.Add(pGroupbyVariableDeclaration);

            // 3. Gropby로 묶은걸 Dictionary로 변환하며 할당한다.
            // 여기서 기본 형식은 다 형변환해야함
            string strParseString = $"p.{strValueFieldName}";
            if(strTypeName == "float")
            {
                strParseString = $"float.Parse({strParseString})";
            }
            else if(strTypeName == "int")
            {
                strParseString = $"int.Parse({strParseString})";
            }
            else if(strTypeFieldName == "string")
            {

            }
            else
            {
                // SpreadSheetParser_MainForm.WriteConsole($"Error Parsing Not Define {strTypeName}");
            }


            CodeMethodInvokeExpression pMethod_Caching = new CodeMethodInvokeExpression(
                new CodeVariableReferenceExpression("arrLocal"), "ToDictionary", new CodeSnippetExpression($"p => p.eGlobalKey, p => {strParseString}"));

            CodeAssignStatement pCachAssign = new CodeAssignStatement(pCasheMemberReference, pMethod_Caching);
            pMethod.Statements.Add(pCachAssign);

            pInitMethod.Statements.Add(new CodeMethodInvokeExpression(
                new CodeMethodReferenceExpression(
                new CodeThisReferenceExpression(), strMethodName)));
        }

        private void Generate_CacheMethod(CodeTypeDeclaration pContainerType, CodeMemberMethod pInitMethod, string strListDataName, string strMapFieldName, string strCacheFieldName, bool bIsOverlapKey)
        {
            string strMethodName = $"Init_{strMapFieldName}";
            var pMethod = pContainerType.AddMethod(strMethodName);
            pMethod.Attributes = MemberAttributes.Private | MemberAttributes.Final;

            CodeFieldReferenceExpression pCasheMemberReference =
                new CodeFieldReferenceExpression(
                new CodeThisReferenceExpression(), strMapFieldName);

            CodeTypeReferenceExpression pField_List = new CodeTypeReferenceExpression($"{strListDataName}");
            if (bIsOverlapKey)
            {
                CodeMethodInvokeExpression pMethod_CachingLocal = new CodeMethodInvokeExpression(
                    pField_List, "GroupBy", new CodeSnippetExpression($"x => x.{strCacheFieldName}"));

                CodeVariableDeclarationStatement pGroupbyVariableDeclaration = new CodeVariableDeclarationStatement(
                    "var", "arrLocal", pMethod_CachingLocal);

                pMethod.Statements.Add(pGroupbyVariableDeclaration);

                CodeMethodInvokeExpression pMethod_Caching = new CodeMethodInvokeExpression(
                    new CodeVariableReferenceExpression("arrLocal"), "ToDictionary", new CodeSnippetExpression($"g => g.Key, g => g.ToList()"));

                CodeAssignStatement pCachAssign = new CodeAssignStatement(pCasheMemberReference, pMethod_Caching);
                pMethod.Statements.Add(pCachAssign);
            }
            else
            {
                CodeMethodInvokeExpression pMethod_Caching = new CodeMethodInvokeExpression(
                    pField_List, "ToDictionary", new CodeSnippetExpression($"x => x.{strCacheFieldName}"));

                CodeAssignStatement pCachAssign = new CodeAssignStatement(pCasheMemberReference, pMethod_Caching);
                pMethod.Statements.Add(pCachAssign);
            }

            pInitMethod.Statements.Add(new CodeMethodInvokeExpression(
                new CodeMethodReferenceExpression(
                new CodeThisReferenceExpression(), strMethodName)));
        }


#if !UNITY_EDITOR
        public override void DoWorkAfter()
        {
            const string const_BuildMethodeName = "UnitySO_Generator.DoBuild";
            if(string.IsNullOrEmpty(strUnityEditorPath) == false)
                System.Diagnostics.Process.Start(strUnityEditorPath, $"-quit -batchmode -executeMethod {const_BuildMethodeName}");

            if (bOpenPath_AfterBuild_CSharp)
                DoOpenPath(strExportPath);
        }

        protected override void OnShowForm(Form pFormInstance)
        {
            Work_Generate_Unity_ScriptableObjectForm pForm = (Work_Generate_Unity_ScriptableObjectForm)pFormInstance;
            pForm.DoInit(this);
        }
#endif
    }

}