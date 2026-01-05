# Certify SSL Manager - Python DNS Helper

import sys
import os
import getopt
import logging

# add script path to module search path
# embedded libs module path (libcloud)
sys.path.append("C:\\ProgramData\\Certify\\python-embedded\\lib")

# script module path
sys.path.append(os.path.abspath(".") + "\\")

#print (sys.path())

import dns_helper


def main(argv):

    provider = "ROUTE53"
    credentials = "username,pwd"
    domain = "example.com"
    txt_record_name = ""
    txt_record_value = ""

    # init
    logging.info("Certify Certificate Manager - Python/Libcloud DNS Helper.")

    opts, args = getopt.getopt(argv, "h:p:c:d:n:v:", [])

    for opt, arg in opts:
        if opt == '-h':
            print (
                'dns_helper_init.py -p <providername> -c <user,pwd> -d <domain> -n <record name> -v <record value>')
            sys.exit()
        elif opt in ("-p"):
            provider = arg
        elif opt in ("-c"):
            credentials = arg
        elif opt in ("-d"):
            domain = arg
        elif opt in ("-n"):
            txt_record_name = arg
        elif opt in ("-v"):
            txt_record_value = arg

    helper = dns_helper.dns_helper(provider, credentials)

    # add/update txt record value
    helper.update_txt_record(domain, txt_record_name, txt_record_value)


#########################################
if __name__ == "__main__":
    main(sys.argv[1:])
