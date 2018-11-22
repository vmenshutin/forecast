using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Odbc;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Forecast
{
    public partial class Form1 : Form
    {
        OdbcConnection connection = new OdbcConnection();
        bool areGridColumnsStyled = false;

        public Form1()
        {
            InitializeComponent();

            // read connection string and open the connection
            connection.ConnectionString = ReadConfig();
            connection.Open();

            // populate location combobox
            PopulateLocationComboBox();

            // set basic grid styling
            SetDataGridViewStyleProps(SupplierDataGridView);
            SetDataGridViewStyleProps(ItemsGridView);

            // event listeners
            ItemsGridView.CellMouseClick += CellMouseClick;
            SupplierDataGridView.CellMouseClick += CellMouseClick;
        }

        private void CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var dgv = (DataGridView)sender;
            var columnName = dgv.Columns[e.ColumnIndex].Name;

            if (columnName == "STOCK" || columnName == "SUPPLIERNO")
            {
                var wait = ShowWaitForm();

                StringBuilder sb = new StringBuilder();
                //Starting Information for process like its path, use system shell i.e. control process by system etc.
                var psi = new ProcessStartInfo(@"C:\WINDOWS\system32\cmd.exe")
                {
                    // its states that system shell will not be used to control the process instead program will handle the process
                    UseShellExecute = false,
                    ErrorDialog = false,
                    // Do not show command prompt window separately
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    //redirect all standard inout to program
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true
                };
                //create the process with above infor and start it
                Process plinkProcess = new Process
                {
                    StartInfo = psi
                };
                plinkProcess.Start();
                //link the streams to standard inout of process
                StreamWriter inputWriter = plinkProcess.StandardInput;
                StreamReader outputReader = plinkProcess.StandardOutput;
                StreamReader errorReader = plinkProcess.StandardError;
                //send command to cmd prompt and wait for command to execute with thread sleep
                if (e.RowIndex != -1)
                {
                    var line = columnName == "STOCK"
                        ? @"START exo://stockitem/?stockcode=" + dgv.Rows[e.RowIndex].Cells[columnName].Value.ToString()
                        : @"START exo://craccount(" + dgv.Rows[e.RowIndex].Cells[columnName].Value.ToString() + ")";
                    inputWriter.WriteLine(line);
                }

                wait.Close();
            }
        }

        private void SupplierDataGridView_RowEnter(object sender, DataGridViewCellEventArgs e)
        {
            var selectedRows = ((DataGridView)sender).SelectedRows;

            if (selectedRows.Count > 0)
            {
                var cells = selectedRows[0].Cells;

                BindingSource bs = new BindingSource();
                bs.DataSource = ItemsGridView.DataSource;
                bs.Filter = "SUPPLIERNO=" + cells["SUPPLIERNO"].Value.ToString();
                ItemsGridView.DataSource = bs;
            }
        }

        // reads config txt
        private string ReadConfig()
        {
            using (StreamReader sr = new StreamReader("config.txt"))
            {
                string connectionString = sr.ReadToEnd();
                int serverNamePrefixIndex = connectionString.IndexOf("Data Source=");
                int serverNameIndex = serverNamePrefixIndex + 12;
                int databaseNamePrefixIndex = connectionString.IndexOf(";Initial Catalog=");

                string serverName = connectionString.Substring(serverNameIndex, databaseNamePrefixIndex - serverNameIndex);

                int databaseNameIndex = databaseNamePrefixIndex + 17;
                int securityPrefixIndex = connectionString.IndexOf(";Persist Security Info=");

                string databaseName = connectionString.Substring(databaseNameIndex, securityPrefixIndex - databaseNameIndex);

                int loginPrefixIndex = connectionString.IndexOf("User ID=");
                int loginIndex = loginPrefixIndex + 8;
                int passwordPrefixIndex = connectionString.IndexOf(";Password=");

                string loginName = connectionString.Substring(loginIndex, passwordPrefixIndex - loginIndex);

                int passwordIndex = passwordPrefixIndex + 10;
                int passwordEndIndex = connectionString.IndexOf("\"\r\n\r\n#");

                string passwordName = connectionString.Substring(passwordIndex, passwordEndIndex - passwordIndex);

                return "Driver={SQL Server};Server=" + serverName + ";UID=" + loginName + ";PWD=" + passwordName + ";Database=" + databaseName + ";";
            }
        }

        // set major style properties for datagridview
        private void SetDataGridViewStyleProps(DataGridView dgv)
        {
            dgv.EditMode = DataGridViewEditMode.EditOnEnter;
            dgv.CellBorderStyle = DataGridViewCellBorderStyle.Raised;
            dgv.MultiSelect = true;
            dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgv.DefaultCellStyle.SelectionBackColor = Color.Wheat;
            dgv.DefaultCellStyle.SelectionForeColor = dgv.DefaultCellStyle.ForeColor;
            dgv.DefaultCellStyle.Font = new Font("Arial", 11F, FontStyle.Regular, GraphicsUnit.Pixel);
            dgv.BorderStyle = BorderStyle.Fixed3D;
            dgv.RowHeadersVisible = false;
            dgv.AllowUserToAddRows = false;
            dgv.AllowUserToResizeRows = false;
            dgv.AllowUserToDeleteRows = false;

            //Disable custom column sorting for all splits
            foreach (DataGridViewColumn column in dgv.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
        }

        private void Calculate()
        {
            var wait = ShowWaitForm();

            SupplierDataGridView.RowEnter -= SupplierDataGridView_RowEnter;

            (new OdbcCommand("exec calculate_forecast "
                + (DemandCheckBox.Checked ? "1" : "0") + ", "
                + (WebCheckBox.Checked ? "1" : "0") + ", '"
                + LocationComboBox.SelectedValue.ToString() + "'", connection)).ExecuteNonQuery();

            // populate supplier grid
            var supplierAd = new OdbcDataAdapter("select distinct SUPPLIERNO, accs.NAME from X_FORECAST_TEMP temp LEFT JOIN CR_ACCS accs on temp.SUPPLIERNO = accs.ACCNO order by accs.NAME", connection);
            var supplierDataSet = new DataSet();
            OdbcCommandBuilder supplierCmdBuilder = new OdbcCommandBuilder(supplierAd);
            supplierAd.Fill(supplierDataSet);

            SupplierDataGridView.DataSource = supplierDataSet.Tables[0];
            // end of populate supplier grid

            // populate items grid
            var itemsAd = new OdbcDataAdapter("select * from X_FORECAST_TEMP order by STOCK", connection);
            var itemsDataSet = new DataSet();
            OdbcCommandBuilder itemsCmdBuilder = new OdbcCommandBuilder(itemsAd);
            itemsAd.Fill(itemsDataSet);

            ItemsGridView.DataSource = itemsDataSet.Tables[0];
            // end of populate items grid

            SupplierDataGridView.RowEnter += SupplierDataGridView_RowEnter;
            SupplierDataGridView.Focus();

            wait.Close();
        }

        private void CalculateBtn_Click(object sender, EventArgs e)
        {
            if (DemandCheckBox.Checked || WebCheckBox.Checked)
            {
                Calculate();

                if (!areGridColumnsStyled)
                {
                    StyleGridColumns();
                    areGridColumnsStyled = true;
                }
            }
        }

        private void PopulateLocationComboBox()
        {
            var locationAdapter = new OdbcDataAdapter("SELECT CONCAT(LOCNO, ' ', LCODE) FROM STOCK_LOCATIONS", connection);
            var locationDS = new DataSet();
            OdbcCommandBuilder cmdbuilder = new OdbcCommandBuilder(locationAdapter);
            locationAdapter.Fill(locationDS);

            DataRow[] rows = locationDS.Tables[0].Select();
            string[] optionsArray = rows.Select(row => row[0].ToString()).ToArray();

            LocationComboBox.DataSource = optionsArray.Clone();
        }

        private void StyleGridColumns()
        {
            SupplierDataGridView.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;
            SupplierDataGridView.Columns[0].MinimumWidth = 100;
            SupplierDataGridView.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            SupplierDataGridView.Columns[0].DefaultCellStyle.Font = new Font("Arial", 11F, FontStyle.Underline, GraphicsUnit.Pixel);
            SupplierDataGridView.Columns[0].DefaultCellStyle.ForeColor = Color.Blue;
            SupplierDataGridView.Columns[0].DefaultCellStyle.SelectionForeColor = Color.Blue;

            ItemsGridView.Columns["SUPPLIERNO"].Visible = false;

            ItemsGridView.Columns["STOCK"].AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;
            ItemsGridView.Columns["STOCK"].MinimumWidth = 100;
            ItemsGridView.Columns["STOCK"].DefaultCellStyle.Font = new Font("Arial", 11F, FontStyle.Underline, GraphicsUnit.Pixel);
            ItemsGridView.Columns["STOCK"].DefaultCellStyle.ForeColor = Color.Blue;
            ItemsGridView.Columns["STOCK"].DefaultCellStyle.SelectionForeColor = Color.Blue;

            ItemsGridView.Columns["DESCRIPTION"].MinimumWidth = 400;
        }

        private PleaseWaitForm ShowWaitForm()
        {
            PleaseWaitForm wait = new PleaseWaitForm();
            wait.Location = new Point(Location.X + (Width - wait.Width) / 2, Location.Y + (Height - wait.Height) / 2);
            wait.Show();

            System.Windows.Forms.Application.DoEvents();

            return wait;
        }
    }
}
