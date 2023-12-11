namespace GaussDB
{
    internal enum GaussDBAuthenticationPasswordType
    {
        /// <summary>
        /// 
        /// </summary>
        PLAIN_PASSWORD = 0,
        /// <summary>
        /// 
        /// </summary>
        MD5_PASSWORD = 1,
        /// <summary>
        /// 
        /// </summary>
        SHA256_PASSWORD = 2,
        /// <summary>
        /// 
        /// </summary>
        ERROR_PASSWORD = 3,
        /// <summary>
        /// 
        /// </summary>
        BAD_MEM_ADDR = 4,
        /// <summary>
        /// 
        /// </summary>
        COMBINED_PASSWORD = 5,
        /// <summary>
        /// 
        /// </summary>
        SM3_PASSWORD = 6,
    }
}
