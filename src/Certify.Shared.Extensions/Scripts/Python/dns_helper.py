# Certify SSL Manager - Python DNS Helper
# using apache libcloud
# with some inspiration from https://github.com/ArroyoNetworks/asyncme

import logging
from libcloud.dns import providers
from libcloud.dns.types import RecordType

logging.basicConfig(level=logging.INFO)


class dns_helper:
    """
    Helper class to create/delete txt records from a DNS zone
    """

    def __init__(self, provider_name, credential_args):

        dns_provider = providers.get_driver(provider_name.lower())

        credential_list = [x.strip() for x in credential_args.split(',')]
        credentials = (credential_list[0], credential_list[1])

        self.dns_API = dns_provider(*credentials)

    def find_zone_recursively(self, domain):
        # attempt to find zone for example.subdomain.domain.com, then if no luck find zone for subdomain.domain.com etc
        zone = self.find_zone(domain)
        if zone:
            return zone
        else:
            # zone not found, may be too specific, zone cut domain name e.g. test.domain.com > domain.com
            zonecut = str(domain.partition(".")[2])
            return self.find_zone_recursively(zonecut)

    def find_zone(self, domain):
        # will print a message to the console
        logging.info("Finding dns zone for domain: {}".format(domain))
        zone_list = self.dns_API.list_zones()

        matched_zones = [z for z in zone_list if z.domain ==
                         domain or z.domain == domain + "."]

        if len(matched_zones) == 1:
            return matched_zones[0]
        else:
            return None

    def find_txt_record(self, zone, record_name):
        adjusted_record_name = self.get_recordname_adjusted(zone, record_name)

        try:
            record_list = self.dns_API.list_records(zone)
            # for x in record_list:
            #    print x
            return next(r for r in record_list if r.name == adjusted_record_name)
        except StopIteration:
            return None

    def delete_txt_record(self, zone, target_domain, record_name):
        if (zone is None):
            zone = self.find_zone_recursively(target_domain)

        if zone:
            record = self.find_txt_record(zone, record_name)
            if record:
                record.delete()
                logging.info("TXT record deleted: {}".format(record_name))
            else:
                logging.debug(
                    "Record not found, could not delete:{}".format(record_name))
        else:
            logging.warning("No zone found: {}".format(target_domain))

    def get_recordname_adjusted(self, zone, record_name):
        # for record "example.test.domain.com" in zone domain.com. return the "example.test" portion of the record name
        zone_withoutdot = zone.domain.rstrip(".")
        adjusted_record_name = record_name

        if (str(record_name).endswith(zone_withoutdot)):
            adjusted_record_name = record_name.replace(
                zone_withoutdot, "").rstrip(".")

        return adjusted_record_name

    def create_txt_record(self, zone, target_domain, record_name, record_value):

        if (zone is None):
            zone = self.find_zone_recursively(target_domain)

        if zone:
            logging.info("Found Zone: {}".format(zone))

            logging.info("Creating TXT record [{}]  with value [{}]".format(
                record_name, record_value))

            adjusted_record_name = self.get_recordname_adjusted(
                zone, record_name)

            try:
                result = zone.create_record(
                    name=adjusted_record_name, type=RecordType.TXT, data=record_value)
                return result
            except:
                logging.warning(
                    "Could not create TXT record. Record exists or access denied.")
                return None
        else:
            logging.warning("No zone found: {}".format(target_domain))
            return None

    def update_txt_record(self, target_domain, record_name, record_value):
        # find zone for this domain
        zone = self.find_zone_recursively(target_domain)

        # delete record if it exists
        self.delete_txt_record(zone, target_domain, record_name)

        # create new record
        self.create_txt_record(zone, target_domain, record_name, record_value)
