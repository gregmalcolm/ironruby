require File.dirname(__FILE__) + '/../../spec_helper'
require File.dirname(__FILE__) + '/fixtures/classes'
require File.dirname(__FILE__) + '/shared/write'

describe "IO#write_nonblock on a file" do
  before :each do
    @filename = tmp("IO_syswrite_file") + $$.to_s
    File.open(@filename, "w") do |file|
      file.write("012345678901234567890123456789")
    end
    @file = File.open(@filename, "r+")
    @readonly_file = File.open(@filename)
  end

  after :each do
    @file.close
    @readonly_file.close
    File.delete(@filename)
  end

  platform_is_not :windows do
    it "writes all of the string's bytes but does not buffer them" do
      written = @file.write_nonblock("abcde")
      written.should == 5
      File.open(@filename) do |file|
        file.sysread(10).should == "abcde56789"
        file.seek(0)
        @file.fsync
        file.sysread(10).should == "abcde56789"
      end
    end

    it "checks if the file is writable if writing zero bytes" do
      lambda { @readonly_file.write_nonblock("") }.should raise_error
    end
  end
  
  platform_is :windows do
    it "raises Errno::EBADF" do
      lambda { @file.write_nonblock("abcde") }.should raise_error(Errno::EBADF)
    end
  end
end

describe "IO#write_nonblock" do
  platform_is_not :windows do
    it_behaves_like :io_write, :write_nonblock
  end
end
