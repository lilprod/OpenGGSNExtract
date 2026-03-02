using Microsoft.Extensions.Options;
using Renci.SshNet;
using System;
using System.IO;

public class FtpHelper
{
    private readonly ExtractConfig _config;

    public FtpHelper(IOptions<ExtractConfig> config)
    {
        _config = config.Value;
    }

    public bool SendFile(string localPath, string remoteDir)
    {
        try
        {
            using var client = new SftpClient(
                _config.SftpSettings.Host,
                _config.SftpSettings.Port,
                _config.SftpSettings.Username,
                _config.SftpSettings.Password);

            client.Connect();

            if (!client.Exists(remoteDir))
            {
                client.CreateDirectory(remoteDir);
            }

            using var fileStream = new FileStream(localPath, FileMode.Open);
            string remotePath = remoteDir + "/" + Path.GetFileName(localPath);

            client.UploadFile(fileStream, remotePath);
            client.Disconnect();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
